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
public sealed record PasskeyCredential(
    string UserEmail,
    string UserHandle,
    string CredentialId,
    string PublicKey,
    uint SignatureCounter,
    Guid AaGuid,
    string Nickname,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastUsedAtUtc);

/// <summary>
/// Lightweight projection of <see cref="PasskeyCredential"/> safe to return to
/// the browser (no public-key material, no user handle).
/// </summary>
/// <param name="CredentialId">Authenticator-provided credential id (base64url).</param>
/// <param name="Nickname">User-friendly label.</param>
/// <param name="CreatedAtUtc">When this credential was registered.</param>
/// <param name="LastUsedAtUtc">When this credential was last used to sign in.</param>
public sealed record PasskeyDescriptor(
    string CredentialId,
    string Nickname,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastUsedAtUtc);
