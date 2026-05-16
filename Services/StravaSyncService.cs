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
    private readonly StravaOptions _options;
    private readonly ILogger<StravaSyncService> _logger;

    /// <summary>
    /// Initialises the orchestrator with its collaborators.
    /// </summary>
    /// <param name="api">Strava REST transport.</param>
    /// <param name="tokens">OAuth lifecycle helper.</param>
    /// <param name="videos">Gallery repository for creating / updating items.</param>
    /// <param name="options">Bound Strava options (privacy filter etc.).</param>
    /// <param name="logger">Structured logger for diagnostic events.</param>
    public StravaSyncService(
        IStravaApiClient api,
        StravaTokenService tokens,
        IVideoRepository videos,
        IOptions<StravaOptions> options,
        ILogger<StravaSyncService> logger)
    {
        _api = api;
        _tokens = tokens;
        _videos = videos;
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
            existing.Training = StravaActivityMapper.ToTrainingData(activity);
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

        video.Training = StravaActivityMapper.ToTrainingData(fetched.Value);
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
        var training = StravaActivityMapper.ToTrainingData(activity);
        var category = StravaActivityMapper.ResolveCategory(activity);
        var video = Video.Create(
            id: _videos.GenerateNextId(),
            title: string.IsNullOrWhiteSpace(activity.Name)
                ? $"Strava {activity.Type} {activity.StartDate:yyyy-MM-dd}"
                : activity.Name,
            description: activity.Description ?? string.Empty,
            fileName: string.Empty,
            location: null,
            category: category,
            fileSizeBytes: 0);

        video.UploadedAt = training.StartTimeUtc;
        video.Training = training;
        await _videos.SaveAsync(video);
        _logger.LogInformation(
            "Created gallery item {VideoId} from Strava activity {ActivityId} (category {Category})",
            video.Id, activity.Id, category);
        return video;
    }

    private static bool IsPublic(StravaActivity activity) =>
        string.Equals(activity.Visibility, "everyone", StringComparison.OrdinalIgnoreCase);
}
