namespace MyHomePage.Options;

/// <summary>
/// Strongly-typed configuration for the Strava OAuth integration.
/// Bound from the "Strava" section of appsettings.json. Secrets
/// (<see cref="ClientSecret"/>, <see cref="WebhookVerifyToken"/>) are
/// expected to come from environment variables / user secrets, never
/// from the committed configuration file.
/// </summary>
public sealed class StravaOptions
{
    /// <summary>Configuration section name in appsettings.json.</summary>
    public const string SectionName = "Strava";

    /// <summary>OAuth client id from the Strava developer console.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>OAuth client secret from the Strava developer console.</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Fully-qualified callback URL registered with Strava. Must match the
    /// "Authorization Callback Domain" configured in the developer console.
    /// Example: https://mountains.cruxbeta.net/auth/strava/callback.
    /// </summary>
    public string RedirectUri { get; set; } = string.Empty;

    /// <summary>
    /// OAuth scopes requested at consent time. Defaults to read + read_all
    /// so the integration can also import private activities the user owns.
    /// </summary>
    public string Scope { get; set; } = "read,activity:read_all";

    /// <summary>
    /// Shared secret echoed back to Strava during webhook subscription
    /// verification (the GET hub.verify_token handshake). Pick any random
    /// non-empty string and use the same value when creating the subscription.
    /// </summary>
    public string WebhookVerifyToken { get; set; } = string.Empty;

    /// <summary>
    /// When true (default), only activities with Strava visibility set to
    /// "everyone" are auto-imported by the webhook. Private / followers-only
    /// activities can still be attached manually from the admin page.
    /// </summary>
    public bool ImportPublicOnly { get; set; } = true;
}
