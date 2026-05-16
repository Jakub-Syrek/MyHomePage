using MyHomePage.Models;

namespace MyHomePage.Abstractions;

/// <summary>
/// Application-level orchestration of Strava → gallery synchronisation:
/// fetches an activity, maps it to a <see cref="TrainingData"/> record,
/// resolves the matching gallery category and either attaches the data to
/// an existing gallery item or creates a new placeholder item.
///
/// Wraps the lower-level <see cref="IStravaApiClient"/> /
/// <see cref="IVideoRepository"/> so endpoints / UI never need to know the
/// transport details.
/// </summary>
public interface IStravaSyncService
{
    /// <summary>
    /// Imports a single Strava activity into the gallery, creating a new
    /// placeholder item when no existing one matches.
    /// </summary>
    /// <param name="activityId">Strava activity identifier.</param>
    /// <param name="enforcePrivacyFilter">
    /// When true (the default for webhook callers), activities that fail the
    /// <see cref="MyHomePage.Options.StravaOptions.ImportPublicOnly"/> check
    /// are rejected. Manual admin imports pass false so the operator can
    /// pull any activity they have read access to on Strava.
    /// </param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    Task<OperationResult<Video>> ImportActivityAsync(
        long activityId,
        bool enforcePrivacyFilter = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Attaches an already-fetched Strava activity to an existing gallery item.
    /// </summary>
    /// <param name="videoId">Identifier of the gallery item to update.</param>
    /// <param name="activityId">Strava activity identifier.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    Task<OperationResult<Video>> AttachActivityToVideoAsync(
        int videoId,
        long activityId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the authenticated athlete's most recent activities so the admin
    /// UI can offer them for manual attach.
    /// </summary>
    /// <param name="page">1-based page number.</param>
    /// <param name="perPage">Page size (max 30).</param>
    /// <param name="cancellationToken">Token used to cancel the call.</param>
    Task<OperationResult<IReadOnlyList<StravaActivity>>> ListRecentActivitiesAsync(
        int page = 1,
        int perPage = 30,
        CancellationToken cancellationToken = default);
}
