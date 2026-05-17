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
    private readonly IStravaApiClient _api;
    private readonly StravaTokenService _tokens;
    private readonly IVideoRepository _videos;
    private readonly IAiAssistantService _ai;
    private readonly IReverseGeocoder _geocoder;
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
    /// <param name="options">Bound Strava options (privacy filter etc.).</param>
    /// <param name="logger">Structured logger for diagnostic events.</param>
    public StravaSyncService(
        IStravaApiClient api,
        StravaTokenService tokens,
        IVideoRepository videos,
        IAiAssistantService ai,
        IReverseGeocoder geocoder,
        IOptions<StravaOptions> options,
        ILogger<StravaSyncService> logger)
    {
        _api = api;
        _tokens = tokens;
        _videos = videos;
        _ai = ai;
        _geocoder = geocoder;
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

        var existing = await FindExistingAsync(activity.Id, cancellationToken);
        if (existing is not null)
        {
            var gear = await TryFetchGearAsync(activity, cancellationToken);
            existing.Training = StravaActivityMapper.ToTrainingData(activity, gear);
            await _videos.SaveAsync(existing);
            _logger.LogInformation(
                "Updated existing video {VideoId} with Strava activity {ActivityId}",
                existing.Id, activity.Id);
            return OperationResult<Video>.Success(existing);
        }

        var created = await CreatePlaceholderAsync(activity, cancellationToken);
        return OperationResult<Video>.Success(created);
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
        return all.FirstOrDefault(v =>
            v.Training is not null &&
            v.Training.Source == TrainingSource.Strava &&
            v.Training.ExternalId == external);
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
        var video = Video.Create(
            id: _videos.GenerateNextId(),
            title: string.IsNullOrWhiteSpace(activity.Name)
                ? $"Strava {activity.Type} {activity.StartDate:yyyy-MM-dd}"
                : activity.Name,
            description: activity.Description ?? string.Empty,
            fileName: string.Empty,
            location: location,
            category: category,
            fileSizeBytes: 0,
            latitude: lat,
            longitude: lng);

        video.UploadedAt = training.StartTimeUtc;
        video.Training = training;
        await _videos.SaveAsync(video);
        _logger.LogInformation(
            "Created gallery item {VideoId} from Strava activity {ActivityId} " +
            "(category {Category}, location '{Location}', GPS {Lat:F4},{Lng:F4})",
            video.Id, activity.Id, category, location ?? "<unknown>", lat ?? 0, lng ?? 0);
        return video;
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
