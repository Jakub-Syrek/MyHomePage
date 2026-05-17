using MyHomePage.Models;

namespace MyHomePage.Abstractions;

/// <summary>
/// Persistence boundary for registered WebAuthn passkeys. Abstracted so the
/// storage backend (file, key vault, database) can evolve without rippling
/// through callers (Dependency Inversion Principle).
/// </summary>
public interface IPasskeyStore
{
    /// <summary>
    /// Returns all credentials registered against the supplied email, ordered
    /// from oldest to newest. Empty list when the user has no passkeys yet.
    /// </summary>
    /// <param name="email">User email (case-insensitive lookup key).</param>
    /// <param name="cancellationToken">Cancels the I/O.</param>
    Task<IReadOnlyList<PasskeyCredential>> GetByEmailAsync(
        string email,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the single credential identified by <paramref name="credentialId"/>,
    /// or null when no such credential is registered. Used during the assertion
    /// (login) flow to look up the public key.
    /// </summary>
    /// <param name="credentialId">Base64url-encoded credential id.</param>
    /// <param name="cancellationToken">Cancels the I/O.</param>
    Task<PasskeyCredential?> GetByCredentialIdAsync(
        string credentialId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns every credential registered against the supplied user handle.
    /// Used during userless login flows where the authenticator returns the
    /// user handle and the relying party needs to discover the matching email.
    /// </summary>
    /// <param name="userHandle">Base64url-encoded user handle.</param>
    /// <param name="cancellationToken">Cancels the I/O.</param>
    Task<IReadOnlyList<PasskeyCredential>> GetByUserHandleAsync(
        string userHandle,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists a freshly registered credential. Throws if the credential id
    /// is already present.
    /// </summary>
    /// <param name="credential">Credential to add.</param>
    /// <param name="cancellationToken">Cancels the I/O.</param>
    Task AddAsync(
        PasskeyCredential credential,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the persisted record for a credential, typically to bump the
    /// signature counter or update the last-used timestamp after a successful
    /// assertion.
    /// </summary>
    /// <param name="credential">Credential with updated fields.</param>
    /// <param name="cancellationToken">Cancels the I/O.</param>
    Task UpdateAsync(
        PasskeyCredential credential,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the credential identified by <paramref name="credentialId"/>.
    /// No-op when the id is not present.
    /// </summary>
    /// <param name="credentialId">Base64url-encoded credential id.</param>
    /// <param name="cancellationToken">Cancels the I/O.</param>
    Task DeleteAsync(
        string credentialId,
        CancellationToken cancellationToken = default);
}
