namespace MyHomePage.Models;

/// <summary>
/// Persisted Strava OAuth tokens for the application's single owner account.
/// Stored on disk via <see cref="MyHomePage.Abstractions.IStravaTokenStore"/>
/// and refreshed automatically once <see cref="ExpiresAtUtc"/> is reached.
/// </summary>
public sealed record StravaTokenSet
{
    /// <summary>Strava athlete id the tokens belong to.</summary>
    public long AthleteId { get; init; }

    /// <summary>Short-lived access token used as bearer for API calls.</summary>
    public string AccessToken { get; init; } = string.Empty;

    /// <summary>Long-lived refresh token used to mint new access tokens.</summary>
    public string RefreshToken { get; init; } = string.Empty;

    /// <summary>UTC expiration timestamp of <see cref="AccessToken"/>.</summary>
    public DateTime ExpiresAtUtc { get; init; }

    /// <summary>OAuth scopes granted on consent (CSV).</summary>
    public string Scope { get; init; } = string.Empty;

    /// <summary>True when <see cref="AccessToken"/> is past its expiration.</summary>
    public bool IsExpired => DateTime.UtcNow >= ExpiresAtUtc - TimeSpan.FromMinutes(1);
}
