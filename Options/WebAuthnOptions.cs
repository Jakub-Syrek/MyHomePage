namespace MyHomePage.Options;

/// <summary>
/// Strongly-typed configuration for the WebAuthn / passkey integration.
/// Bound from the <c>WebAuthn</c> section of <c>appsettings.json</c>. The
/// values control how Fido2NetLib advertises this relying party to
/// authenticators (Windows Hello, Touch ID, hardware keys, ...).
/// </summary>
public sealed class WebAuthnOptions
{
    /// <summary>Configuration section name in appsettings.json.</summary>
    public const string SectionName = "WebAuthn";

    /// <summary>
    /// Effective relying-party identifier (a registrable domain). Must match
    /// the host portion of the URL the user sees in the browser, or be a
    /// suffix of it. Example: <c>mountains.cruxbeta.net</c>. For local
    /// development use <c>localhost</c>.
    /// </summary>
    public string RpId { get; set; } = "localhost";

    /// <summary>
    /// Human-readable relying-party name shown by the authenticator when it
    /// prompts the user (e.g. "Use Windows Hello to sign in to ...").
    /// </summary>
    public string RpName { get; set; } = "My Home Page";

    /// <summary>
    /// Fully-qualified origins (scheme + host[:port]) allowed to initiate
    /// WebAuthn ceremonies for this relying party. Must include every URL
    /// the user actually browses with; mismatches reject the assertion.
    /// </summary>
    public IList<string> Origins { get; set; } = new List<string>
    {
        "http://localhost:5000",
        "https://localhost:5001",
    };

    /// <summary>
    /// Clock-skew tolerance (milliseconds) for assertion timestamps. Default
    /// 5 minutes is the upstream library default.
    /// </summary>
    public int TimestampDriftToleranceMs { get; set; } = 300_000;

    /// <summary>
    /// Maximum allowed user-handle length the relying party will accept from
    /// authenticators. The WebAuthn spec caps this at 64 bytes.
    /// </summary>
    public int MaxUserHandleLength { get; set; } = 64;
}
