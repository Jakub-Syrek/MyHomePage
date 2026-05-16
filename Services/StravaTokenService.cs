using Microsoft.Extensions.Logging;
using MyHomePage.Abstractions;
using MyHomePage.Models;

namespace MyHomePage.Services;

/// <summary>
/// Coordinates OAuth lifecycle for the single account connected to the app:
/// completes the initial code-for-token exchange, persists the resulting
/// token set and transparently refreshes the access token when it expires.
/// Consumers ask for a bearer string and get a valid one without caring
/// whether a refresh happened underneath.
/// </summary>
public sealed class StravaTokenService
{
    private readonly IStravaApiClient _api;
    private readonly IStravaTokenStore _store;
    private readonly ILogger<StravaTokenService> _logger;

    /// <summary>
    /// Initialises the service with its transport and persistence dependencies.
    /// </summary>
    /// <param name="api">Transport boundary used for the token endpoint.</param>
    /// <param name="store">Persistence boundary for the resulting token set.</param>
    /// <param name="logger">Structured logger for diagnostic events.</param>
    public StravaTokenService(
        IStravaApiClient api,
        IStravaTokenStore store,
        ILogger<StravaTokenService> logger)
    {
        _api = api;
        _store = store;
        _logger = logger;
    }

    /// <summary>
    /// Completes the OAuth flow by trading the authorization code for tokens
    /// and persisting them so subsequent API calls can authorise.
    /// </summary>
    /// <param name="authorizationCode">Code from the Strava callback redirect.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    public async Task<OperationResult<StravaTokenSet>> CompleteAuthorizationAsync(
        string authorizationCode,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(authorizationCode))
            return OperationResult<StravaTokenSet>.Failure("Authorization code was not provided.");

        var exchange = await _api.ExchangeCodeAsync(authorizationCode, cancellationToken);
        if (!exchange.IsSuccess || exchange.Value is null)
            return OperationResult<StravaTokenSet>.Failure(exchange.Message);

        var tokens = ToTokenSet(exchange.Value);
        await _store.SaveAsync(tokens, cancellationToken);
        _logger.LogInformation(
            "Strava OAuth completed for athlete {AthleteId}",
            tokens.AthleteId);
        return OperationResult<StravaTokenSet>.Success(tokens);
    }

    /// <summary>
    /// Returns a non-expired bearer access token, refreshing on demand if needed.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>
    /// Failure when no consent is recorded or refresh fails; otherwise a
    /// success result whose value is the access token string.
    /// </returns>
    public async Task<OperationResult<string>> GetValidAccessTokenAsync(
        CancellationToken cancellationToken = default)
    {
        var tokens = await _store.LoadAsync(cancellationToken);
        if (tokens is null)
            return OperationResult<string>.Failure("Strava has not been connected yet.");

        if (!tokens.IsExpired)
            return OperationResult<string>.Success(tokens.AccessToken);

        var refreshed = await RefreshAsync(tokens, cancellationToken);
        return !refreshed.IsSuccess || refreshed.Value is null
            ? OperationResult<string>.Failure(refreshed.Message)
            : OperationResult<string>.Success(refreshed.Value.AccessToken);
    }

    /// <summary>
    /// Disconnects the account by deleting the persisted token set.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    public async Task<OperationResult> DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await _store.DeleteAsync(cancellationToken);
        return OperationResult.Success("Strava disconnected.");
    }

    private async Task<OperationResult<StravaTokenSet>> RefreshAsync(
        StravaTokenSet expired,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(expired.RefreshToken))
            return OperationResult<StravaTokenSet>.Failure("Stored refresh token is empty.");

        var response = await _api.RefreshTokenAsync(expired.RefreshToken, cancellationToken);
        if (!response.IsSuccess || response.Value is null)
            return OperationResult<StravaTokenSet>.Failure(response.Message);

        var renewed = ToTokenSet(response.Value) with
        {
            AthleteId = expired.AthleteId,
            Scope = string.IsNullOrEmpty(response.Value.Scope) ? expired.Scope : response.Value.Scope
        };
        await _store.SaveAsync(renewed, cancellationToken);
        _logger.LogInformation(
            "Strava access token refreshed for athlete {AthleteId}",
            renewed.AthleteId);
        return OperationResult<StravaTokenSet>.Success(renewed);
    }

    private static StravaTokenSet ToTokenSet(StravaTokenResponse response) => new()
    {
        AthleteId = response.Athlete?.Id ?? 0,
        AccessToken = response.AccessToken,
        RefreshToken = response.RefreshToken,
        ExpiresAtUtc = DateTimeOffset.FromUnixTimeSeconds(response.ExpiresAt).UtcDateTime,
        Scope = response.Scope
    };
}
