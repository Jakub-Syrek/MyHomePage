using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyHomePage.Abstractions;
using MyHomePage.Models;
using MyHomePage.Options;

namespace MyHomePage.Services;

/// <summary>
/// Default <see cref="IStravaSyncService"/> implementation. Orchestrates
/// the Strava transport, the OAuth lifecycle and the gallery repository
/// so callers can import an activity with a single call and stay agnostic
/// of HTTP, JSON or persistence concerns.
/// </summary>
public sealed class StravaSyncService : IStravaSyncService
{
    private const string CoverFileName = "cover.jpg";

    /// <summary>
    /// Process-wide gate that serialises every import path
    /// (<see cref="ImportActivityAsync"/>, <see cref="SyncRecentAsync"/>,
    /// <see cref="AttachActivityToVideoAsync"/>). Without this, a Blazor
    /// Server prerender-then-client double init or a webhook firing in
    /// parallel with the page auto-sync would let two threads read the
    /// repository, miss the same external id and then both create their
    /// own gallery item — duplicating the activity. Single-user app, so
    /// a global lock is the right cost / safety trade-off.
    /// </summary>
    private static readonly SemaphoreSlim ImportLock = new(1, 1);

    private readonly IStravaApiClient _api;
    private readonly StravaTokenService _tokens;
    private readonly IVideoRepository _videos;
    private readonly IAiAssistantService _ai;
    private readonly IReverseGeocoder _geocoder;
    private readonly IFileStorageService _storage;
    private readonly StravaOptions _options;
    private readonly ILogger<StravaSyncService> _logger;

    /// <summary>
    /// Initialises the orchestrator with its collaborators.
    /// </summary>
    /// <param name="api">Strava REST transport.</param>
    /// <param name="tokens">OAuth lifecycle helper.</param>
    /// <param name="videos">Gallery repository for creating / updating items.</param>
    /// <param name="ai">AI assistant used as a last-resort location extractor.</param>
    /// <param name="geocoder">Reverse geocoder used when GPS is known but the city is not.</param>
    /// <param name="storage">File storage service used to seed the placeholder cover image.</param>
    /// <param name="options">Bound Strava options (privacy filter etc.).</param>
    /// <param name="logger">Structured logger for diagnostic events.</param>
    public StravaSyncService(
        IStravaApiClient api,
        StravaTokenService tokens,
        IVideoRepository videos,
        IAiAssistantService ai,
        IReverseGeocoder geocoder,
        IFileStorageService storage,
        IOptions<StravaOptions> options,
        ILogger<StravaSyncService> logger)
    {
        _api = api;
        _tokens = tokens;
        _videos = videos;
        _ai = ai;
        _geocoder = geocoder;
        _storage = storage;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<OperationResult<Video>> ImportActivityAsync(
        long activityId,
        bool enforcePrivacyFilter = true,
        CancellationToken cancellationToken = default)
    {
        var fetched = await FetchActivityAsync(activityId, cancellationToken);
        if (!fetched.IsSuccess || fetched.Value is null)
            return OperationResult<Video>.Failure(fetched.Message);

        var activity = fetched.Value;
        if (enforcePrivacyFilter && _options.ImportPublicOnly && !IsPublic(activity))
            return OperationResult<Video>.Failure(
                $"Activity {activity.Id} is not public — skipping per ImportPublicOnly.");

        // Serialised against every other import path: re-checking
        // FindExistingAsync inside the lock makes the dedup atomic so
        // concurrent callers (page auto-sync + webhook, prerender +
        // client init, etc.) never both miss the same external id.
        await ImportLock.WaitAsync(cancellationToken);
        try
        {
            var existing = await FindExistingAsync(activity.Id, cancellationToken);
            if (existing is not null)
            {
                var gear = await TryFetchGearAsync(activity, cancellationToken);
                existing.Training = StravaActivityMapper.ToTrainingData(activity, gear);
                await MigrateCategoryIfNeededAsync(existing, activity, cancellationToken);
                await _videos.SaveAsync(existing);
                _logger.LogInformation(
                    "Updated existing video {VideoId} with Strava activity {ActivityId}",
                    existing.Id, activity.Id);
                return OperationResult<Video>.Success(existing);
            }

            var created = await CreatePlaceholderAsync(activity, cancellationToken);
            return OperationResult<Video>.Success(created);
        }
        finally
        {
            ImportLock.Release();
        }
    }

    /// <summary>
    /// Brings an existing Strava stump back in sync with whatever the
    /// current mapper + category catalogue think it should look like.
    /// Two things drift over time:
    ///
    ///   1) Category — e.g. cycling activities imported before the
    ///      Bicycle category existed went into Running.
    ///   2) Cover image — once the operator (or an earlier migration)
    ///      moves an item between categories, the seeded cover.jpg
    ///      still points at the original category's background and
    ///      becomes visually misleading (a running silhouette on a
    ///      Bicycle stump is the symptom that drove this method in).
    ///
    /// Both are reconciled here. Only pure stumps (no user media
    /// beyond the cover.jpg seed) are touched — anything the operator
    /// has worked on is left alone.
    /// </summary>
    private async Task MigrateCategoryIfNeededAsync(
        Video existing,
        StravaActivity activity,
        CancellationToken cancellationToken)
    {
        var freshCategory = StravaActivityMapper.ResolveCategory(activity);

        // Only act on pure stumps. The moment an operator added a real
        // photo / video, leave the item alone — both category and cover.
        var media = existing.Media ?? new List<MediaItem>();
        var userMedia = media.Count(m =>
            !string.Equals(m.FileName, CoverFileName, StringComparison.OrdinalIgnoreCase));
        if (userMedia > 0)
        {
            if (!string.Equals(existing.Category, freshCategory, StringComparison.Ordinal))
            {
                _logger.LogInformation(
                    "Skipping category migration for video {VideoId}: {Count} user media file(s) attached",
                    existing.Id, userMedia);
            }
            return;
        }

        var categoryChanged = !string.Equals(
            existing.Category, freshCategory, StringComparison.Ordinal);
        if (categoryChanged)
        {
            _logger.LogInformation(
                "Migrating Strava stump {VideoId} from {Old} to {New} (mapper changed)",
                existing.Id, existing.Category, freshCategory);
            existing.Category = freshCategory;
        }

        // Always re-seed the cover for a pure stump. Even when the
        // category did not change this round, the cover.jpg on disk
        // could be stale from a previous category (manual move in the
        // editor, mid-migration crash, schema change). The seed is
        // cheap and idempotent: overwriting the file with the right
        // category's bg is a no-op if it was already correct.
        var newCoverSize = await SeedCoverAsync(existing.Id, freshCategory);
        if (newCoverSize > 0)
        {
            existing.Media = new List<MediaItem>
            {
                MediaItem.Create(CoverFileName, MediaType.Image, newCoverSize, 0)
            };
            existing.FileName = CoverFileName;
            existing.FileSizeBytes = newCoverSize;
        }
        else if (categoryChanged)
        {
            // Migration was requested but the seed asset is missing.
            // Drop the stale cover rather than leave it pointing at
            // the wrong category.
            existing.Media = new List<MediaItem>();
            existing.FileName = string.Empty;
            existing.FileSizeBytes = 0;
        }
        cancellationToken.ThrowIfCancellationRequested();
    }

    /// <inheritdoc />
    public async Task<OperationResult<Video>> AttachActivityToVideoAsync(
        int videoId,
        long activityId,
        CancellationToken cancellationToken = default)
    {
        var video = await _videos.GetByIdAsync(videoId);
        if (video is null)
            return OperationResult<Video>.Failure($"Gallery item {videoId} not found.");

        var fetched = await FetchActivityAsync(activityId, cancellationToken);
        if (!fetched.IsSuccess || fetched.Value is null)
            return OperationResult<Video>.Failure(fetched.Message);

        var gear = await TryFetchGearAsync(fetched.Value, cancellationToken);
        video.Training = StravaActivityMapper.ToTrainingData(fetched.Value, gear);
        await _videos.SaveAsync(video);
        _logger.LogInformation(
            "Attached Strava activity {ActivityId} to existing video {VideoId}",
            activityId, videoId);
        return OperationResult<Video>.Success(video);
    }

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<StravaActivity>>> ListRecentActivitiesAsync(
        int page = 1,
        int perPage = 30,
        CancellationToken cancellationToken = default)
    {
        var token = await _tokens.GetValidAccessTokenAsync(cancellationToken);
        return !token.IsSuccess || token.Value is null
            ? OperationResult<IReadOnlyList<StravaActivity>>.Failure(token.Message)
            : await _api.ListAthleteActivitiesAsync(token.Value, page, perPage, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OperationResult<StravaSyncSummary>> SyncRecentAsync(
        int perPage = 30,
        bool enforcePrivacyFilter = false,
        CancellationToken cancellationToken = default)
    {
        var list = await ListRecentActivitiesAsync(1, perPage, cancellationToken);
        if (!list.IsSuccess || list.Value is null)
            return OperationResult<StravaSyncSummary>.Failure(list.Message);

        var inspected = list.Value.Count;
        var imported = 0;
        var skipped = 0;
        var failed = 0;
        var failures = new List<string>();

        // Per-activity dedup is fresh inside ImportActivityAsync's lock,
        // so we no longer pre-build a hashset out here. That stale-snapshot
        // approach lost the race when two SyncRecentAsync calls overlapped
        // (Blazor Server prerender + client init both fire OnInitializedAsync)
        // and produced duplicate stumps. ImportActivityAsync.FindExistingAsync
        // now runs under ImportLock and is the single source of truth.
        foreach (var activity in list.Value)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Cheap pre-filter against a fresh GetAllAsync snapshot so we
            // skip the activity-detail fetch + ImportLock dance for items
            // we already have. This snapshot can be stale relative to a
            // concurrent caller, but ImportActivityAsync re-checks inside
            // the lock so any miss here is caught by the atomic recheck.
            if (await IsAlreadyImportedAsync(activity.Id, cancellationToken))
            {
                skipped++;
                continue;
            }

            var result = await ImportActivityAsync(
                activity.Id, enforcePrivacyFilter, cancellationToken);
            if (result.IsSuccess)
            {
                imported++;
            }
            else
            {
                failed++;
                failures.Add($"{activity.Id}: {result.Message}");
            }
        }

        _logger.LogInformation(
            "Strava bulk sync: inspected {Inspected}, imported {Imported}, " +
            "skipped {Skipped}, failed {Failed}",
            inspected, imported, skipped, failed);

        return OperationResult<StravaSyncSummary>.Success(
            new StravaSyncSummary(inspected, imported, skipped, failed, failures),
            $"Inspected {inspected}, imported {imported}, skipped {skipped}, failed {failed}.");
    }

    /// <inheritdoc />
    public async Task<int> RefreshStumpCoversAsync(CancellationToken cancellationToken = default)
    {
        await ImportLock.WaitAsync(cancellationToken);
        try
        {
            var all = await _videos.GetAllAsync();
            var stumps = all.Where(IsStravaStump).ToList();

            var refreshed = 0;
            foreach (var stump in stumps)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var coverSize = await SeedCoverAsync(stump.Id, stump.Category);
                if (coverSize <= 0) continue;

                stump.Media = new List<MediaItem>
                {
                    MediaItem.Create(CoverFileName, MediaType.Image, coverSize, 0)
                };
                stump.FileName = CoverFileName;
                stump.FileSizeBytes = coverSize;
                await _videos.SaveAsync(stump);
                refreshed++;
            }

            _logger.LogInformation(
                "Strava stump-cover sweep: {Refreshed} of {Total} stumps re-seeded",
                refreshed, stumps.Count);
            return refreshed;
        }
        finally
        {
            ImportLock.Release();
        }
    }

    /// <summary>
    /// A "stump" is a Strava-imported gallery item that the operator has
    /// not yet attached real photos / videos to — its media list is
    /// either empty or contains nothing but the seeded cover.jpg.
    /// </summary>
    private static bool IsStravaStump(Video v)
    {
        if (v.Training is null || v.Training.Source != TrainingSource.Strava) return false;
        var media = v.Media ?? new List<MediaItem>();
        return media.All(m =>
            string.Equals(m.FileName, CoverFileName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Fresh per-activity dedup check. Re-reads <see cref="IVideoRepository"/>
    /// for every activity so the snapshot reflects writes made by a
    /// concurrent caller earlier in the same sync sweep.
    /// </summary>
    private async Task<bool> IsAlreadyImportedAsync(
        long activityId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var external = activityId.ToString();
        var existing = await FindExistingAsync(activityId, cancellationToken);
        return existing?.Training is not null
            && string.Equals(existing.Training.ExternalId, external, StringComparison.Ordinal);
    }

    private async Task<OperationResult<StravaActivity>> FetchActivityAsync(
        long activityId,
        CancellationToken cancellationToken)
    {
        var token = await _tokens.GetValidAccessTokenAsync(cancellationToken);
        return !token.IsSuccess || token.Value is null
            ? OperationResult<StravaActivity>.Failure(token.Message)
            : await _api.GetActivityAsync(token.Value, activityId, cancellationToken);
    }

    /// <summary>
    /// Best-effort gear lookup: activities carry only <c>gear_id</c>, the
    /// human-readable name lives behind a separate API call. We never let
    /// a failed gear lookup block the import — null is returned and the
    /// mapper writes a null gear label.
    /// </summary>
    private async Task<StravaGear?> TryFetchGearAsync(
        StravaActivity activity,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(activity.GearId)) return null;

        var token = await _tokens.GetValidAccessTokenAsync(cancellationToken);
        if (!token.IsSuccess || token.Value is null) return null;

        var result = await _api.GetGearAsync(token.Value, activity.GearId, cancellationToken);
        if (result.IsSuccess && result.Value is not null) return result.Value;

        _logger.LogDebug(
            "Strava gear {GearId} lookup failed: {Reason}",
            activity.GearId, result.Message);
        return null;
    }

    private async Task<Video?> FindExistingAsync(long activityId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var external = activityId.ToString();
        var all = await _videos.GetAllAsync();

        // Only direct per-activity stumps count as "existing" here.
        // Multi-sport masters reference their constituent activities through
        // Training.SubActivities[] for display, but they DO NOT claim
        // ownership: every Strava activity must be importable as its own
        // stump regardless of any master that links to it. Without this
        // rule the sync would otherwise swallow a re-imported activity
        // whose only mention in the gallery is inside a master, and the
        // user would have no way to bring it back to its sport page.
        var matches = all.Where(v =>
                v.Training is not null &&
                v.Training.Source == TrainingSource.Strava &&
                v.Training.ExternalId == external)
            .ToList();

        if (matches.Count == 0) return null;
        if (matches.Count == 1) return matches[0];

        // Pre-existing duplicates (created before the ImportLock was in
        // place) get cleaned up lazily on the next sync. Keep the item
        // with the most user-added media — it represents the most
        // operator effort. Tie-break on the lowest id (oldest record).
        var canonical = matches
            .OrderByDescending(v => v.Media?.Count ?? 0)
            .ThenBy(v => v.Id)
            .First();

        foreach (var dupe in matches)
        {
            if (dupe.Id == canonical.Id) continue;

            // Only collapse duplicates that have no user-added media on
            // top of the cover.jpg seed — never silently delete an item
            // the operator has actually touched.
            var dupeUserMedia = (dupe.Media ?? new List<MediaItem>())
                .Count(m => !string.Equals(m.FileName, "cover.jpg", StringComparison.OrdinalIgnoreCase));
            if (dupeUserMedia > 0)
            {
                _logger.LogWarning(
                    "Duplicate Strava activity {ActivityId} found on items {KeepId} and {DupeId}, " +
                    "but the duplicate has {Count} user media — leaving it for manual merge",
                    activityId, canonical.Id, dupe.Id, dupeUserMedia);
                continue;
            }

            _logger.LogInformation(
                "Collapsing duplicate Strava stump {DupeId} -> canonical {KeepId} for activity {ActivityId}",
                dupe.Id, canonical.Id, activityId);
            await _videos.DeleteAsync(dupe.Id);
        }

        return canonical;
    }

    private async Task<Video> CreatePlaceholderAsync(
        StravaActivity activity,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var gear = await TryFetchGearAsync(activity, cancellationToken);
        var training = StravaActivityMapper.ToTrainingData(activity, gear);
        var category = StravaActivityMapper.ResolveCategory(activity);
        var (lat, lng) = StravaActivityMapper.ExtractStartCoordinates(activity);
        var location = await ResolveLocationAsync(activity, cancellationToken);
        var videoId = _videos.GenerateNextId();

        // Seed a real cover.jpg by copying the category's static background
        // image so the placeholder renders as a proper photo tile instead
        // of an empty video stub.
        var coverSize = await SeedCoverAsync(videoId, category);
        var mediaItems = coverSize > 0
            ? new List<MediaItem>
            {
                MediaItem.Create(CoverFileName, MediaType.Image, coverSize, 0)
            }
            : new List<MediaItem>();
        var primaryFileName = coverSize > 0 ? CoverFileName : string.Empty;

        var video = Video.Create(
            id: videoId,
            title: string.IsNullOrWhiteSpace(activity.Name)
                ? $"Strava {activity.Type} {activity.StartDate:yyyy-MM-dd}"
                : activity.Name,
            description: activity.Description ?? string.Empty,
            fileName: primaryFileName,
            location: location,
            category: category,
            fileSizeBytes: coverSize,
            media: mediaItems,
            latitude: lat,
            longitude: lng);

        video.UploadedAt = training.StartTimeUtc;
        video.Training = training;
        await _videos.SaveAsync(video);
        _logger.LogInformation(
            "Created gallery item {VideoId} from Strava activity {ActivityId} " +
            "(category {Category}, location '{Location}', GPS {Lat:F4},{Lng:F4}, cover {CoverKB} KB)",
            video.Id, activity.Id, category, location ?? "<unknown>", lat ?? 0, lng ?? 0, coverSize / 1024);
        return video;
    }

    /// <summary>
    /// Copies the category's <c>wwwroot/images/{slug}-bg.jpg</c> into the
    /// new item's directory as <c>cover.jpg</c>. Returns the byte count on
    /// success or 0 when no asset was available — the caller then leaves
    /// the item without primary media (the editor still lets the operator
    /// add files later).
    /// </summary>
    private async Task<long> SeedCoverAsync(int videoId, string category)
    {
        var relative = ResolveCategoryAssetRelativePath(category);
        if (string.IsNullOrEmpty(relative)) return 0;
        try
        {
            return await _storage.CopyWwwRootFileToVideoAsync(
                relative, videoId, CoverFileName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Could not seed placeholder cover for item {Id}", videoId);
            return 0;
        }
    }

    private static string ResolveCategoryAssetRelativePath(string category)
    {
        // VideoCategories.GetPlaceholderImage returns an absolute URL path
        // like "/images/running-bg.jpg" — strip the leading slash to make
        // it a wwwroot-relative path.
        var url = VideoCategories.GetPlaceholderImage(category);
        return url.TrimStart('/');
    }

    /// <summary>
    /// Builds the location label using a five-tier fallback so outdoor
    /// activities without a Strava-supplied city and indoor activities
    /// with the venue buried in the description both end up labelled.
    /// </summary>
    private async Task<string?> ResolveLocationAsync(
        StravaActivity activity,
        CancellationToken cancellationToken)
    {
        // 1. Strava's structured city/state/country fields (auto-filled from GPS).
        var strava = StravaActivityMapper.ExtractLocationLabel(activity);
        if (!string.IsNullOrWhiteSpace(strava)) return strava;

        // 2. Deterministic venue parsing of the activity title (covers
        //    "Avatar Kraków - Push Day" style names).
        var fromTitle = StravaActivityMapper.ExtractVenueFromTitle(activity.Name);
        if (!string.IsNullOrWhiteSpace(fromTitle)) return fromTitle;

        // 3. Reverse-geocode the start coordinates via OpenStreetMap when
        //    Strava skipped the city but the activity does carry GPS
        //    (either start_latlng or the polyline first point).
        var (lat, lng) = StravaActivityMapper.ExtractStartCoordinates(activity);
        if (lat is not null && lng is not null)
        {
            try
            {
                var fromGeocode = await _geocoder.ResolveAsync(
                    lat.Value, lng.Value, cancellationToken);
                if (!string.IsNullOrWhiteSpace(fromGeocode)) return fromGeocode;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex,
                    "Reverse geocoding failed for activity {ActivityId}",
                    activity.Id);
            }
        }

        // 4. AI fallback — Claude reads the title + description and returns
        //    the most likely venue when one is mentioned in prose.
        if (_ai.IsEnabled)
        {
            try
            {
                var fromAi = await _ai.ExtractLocationAsync(
                    activity.Name ?? string.Empty,
                    activity.Description ?? string.Empty,
                    activity.SportType ?? activity.Type ?? string.Empty,
                    cancellationToken);
                if (!string.IsNullOrWhiteSpace(fromAi)) return fromAi;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "AI location extraction failed for activity {ActivityId}",
                    activity.Id);
            }
        }

        // 5. Give up — the editor lets the user fill it in manually.
        return null;
    }

    private static bool IsPublic(StravaActivity activity) =>
        string.Equals(activity.Visibility, "everyone", StringComparison.OrdinalIgnoreCase);
}
