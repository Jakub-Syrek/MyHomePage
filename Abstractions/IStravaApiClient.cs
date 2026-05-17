using MyHomePage.Models;

namespace MyHomePage.Abstractions;

/// <summary>
/// Thin transport boundary around Strava's REST API. Only the calls the
/// integration actually performs are surfaced. Implementations are
/// responsible for HTTP, JSON and bearer-token plumbing — callers see
/// strongly-typed results and an <see cref="OperationResult{T}"/> wrapper
/// per project convention.
/// </summary>
public interface IStravaApiClient
{
    /// <summary>
    /// Exchanges an authorization code for the initial access + refresh token pair.
    /// </summary>
    /// <param name="authorizationCode">Single-use code returned to the OAuth callback URL.</param>
    /// <param name="cancellationToken">Token used to cancel the HTTP call.</param>
    /// <returns>Result containing the raw Strava token response.</returns>
    Task<OperationResult<StravaTokenResponse>> ExchangeCodeAsync(
        string authorizationCode,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Refreshes an expired access token using a previously stored refresh token.
    /// </summary>
    /// <param name="refreshToken">Long-lived refresh token from the persisted token set.</param>
    /// <param name="cancellationToken">Token used to cancel the HTTP call.</param>
    Task<OperationResult<StravaTokenResponse>> RefreshTokenAsync(
        string refreshToken,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches the full activity payload for a single id.
    /// </summary>
    /// <param name="accessToken">Bearer access token to authorize the call.</param>
    /// <param name="activityId">Strava activity identifier.</param>
    /// <param name="cancellationToken">Token used to cancel the HTTP call.</param>
    Task<OperationResult<StravaActivity>> GetActivityAsync(
        string accessToken,
        long activityId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the authenticated athlete's most recent activities (paged).
    /// </summary>
    /// <param name="accessToken">Bearer access token to authorize the call.</param>
    /// <param name="page">1-based page number (Strava default is page 1).</param>
    /// <param name="perPage">Page size, capped at 30 to match the admin UI.</param>
    /// <param name="cancellationToken">Token used to cancel the HTTP call.</param>
    Task<OperationResult<IReadOnlyList<StravaActivity>>> ListAthleteActivitiesAsync(
        string accessToken,
        int page = 1,
        int perPage = 30,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches gear metadata (shoes / bike) by Strava gear id. Activity
    /// payloads carry only <c>gear_id</c>, not the full gear record — this
    /// is the second call needed to display "Hoka Mach 6" next to a run.
    /// </summary>
    /// <param name="accessToken">Bearer access token to authorize the call.</param>
    /// <param name="gearId">Strava gear identifier (e.g. "g123456").</param>
    /// <param name="cancellationToken">Token used to cancel the HTTP call.</param>
    Task<OperationResult<StravaGear>> GetGearAsync(
        string accessToken,
        string gearId,
        CancellationToken cancellationToken = default);
}
