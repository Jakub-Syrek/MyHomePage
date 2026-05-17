namespace MyHomePage.Models;

/// <summary>
/// A single passkey (WebAuthn credential) bound to a user account. Stored on
/// disk between login ceremonies. Only the public key is persisted — the
/// matching private key never leaves the authenticator (Windows Hello, Touch
/// ID, hardware key).
/// </summary>
/// <param name="UserEmail">Email of the user this credential belongs to.</param>
/// <param name="UserHandle">Opaque stable identifier returned to authenticators (base64url).</param>
/// <param name="CredentialId">Authenticator-provided credential id (base64url).</param>
/// <param name="PublicKey">COSE-encoded public key bytes (base64url).</param>
/// <param name="SignatureCounter">Last observed signature counter (replay-attack protection).</param>
/// <param name="AaGuid">Authenticator attestation GUID (identifies the authenticator model).</param>
/// <param name="Nickname">User-friendly label, e.g. "Windows Hello (work laptop)".</param>
/// <param name="CreatedAtUtc">When this credential was registered.</param>
/// <param name="LastUsedAtUtc">When this credential was last used to sign in.</param>
/// <param name="Transports">
/// Transports the authenticator advertised at registration time (Usb, Nfc, Ble,
/// SmartCard, Hybrid, Internal). Used to render a friendly "Type" label in the
/// management UI. Optional / nullable so credentials registered before this
/// field existed still deserialise cleanly.
/// </param>
public sealed record PasskeyCredential(
    string UserEmail,
    string UserHandle,
    string CredentialId,
    string PublicKey,
    uint SignatureCounter,
    Guid AaGuid,
    string Nickname,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastUsedAtUtc,
    IReadOnlyList<string>? Transports = null);

/// <summary>
/// Lightweight projection of <see cref="PasskeyCredential"/> safe to return to
/// the browser (no public-key material, no user handle).
/// </summary>
/// <param name="CredentialId">Authenticator-provided credential id (base64url).</param>
/// <param name="Nickname">User-friendly label.</param>
/// <param name="Type">
/// Friendly authenticator type derived from transports:
/// "Fingerprint / face / PIN" (Internal), "Phone / cross-device" (Hybrid),
/// "Security key (USB/NFC/BLE/SmartCard)" or "Unknown".
/// </param>
/// <param name="CreatedAtUtc">When this credential was registered.</param>
/// <param name="LastUsedAtUtc">When this credential was last used to sign in.</param>
public sealed record PasskeyDescriptor(
    string CredentialId,
    string Nickname,
    string Type,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastUsedAtUtc);

/// <summary>
/// Helpers for turning Fido2NetLib transport metadata into a single
/// human-readable string. Centralised here so the endpoint and the
/// management UI agree on the same labels.
/// </summary>
public static class PasskeyTypeFormatter
{
    /// <summary>Derives a friendly authenticator-type label.</summary>
    /// <param name="transports">Transport strings as stored on the credential.</param>
    /// <returns>One-line label suitable for display.</returns>
    public static string Describe(IReadOnlyList<string>? transports)
    {
        if (transports is null || transports.Count == 0)
        {
            return "Unknown";
        }

        var set = transports
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (set.Contains("Internal"))
        {
            return "Fingerprint / face / PIN";
        }

        if (set.Contains("Hybrid"))
        {
            return "Phone / cross-device";
        }

        var hardware = new List<string>();
        if (set.Contains("Usb")) hardware.Add("USB");
        if (set.Contains("Nfc")) hardware.Add("NFC");
        if (set.Contains("Ble")) hardware.Add("Bluetooth");
        if (set.Contains("SmartCard")) hardware.Add("Smart card");

        return hardware.Count > 0
            ? $"Security key ({string.Join(" / ", hardware)})"
            : "Unknown";
    }
}
