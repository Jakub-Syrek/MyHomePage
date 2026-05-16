using MyHomePage.Models;

namespace MyHomePage.Abstractions;

/// <summary>
/// Persistence boundary for the Strava OAuth token set. Abstracted so the
/// rest of the integration can be unit-tested without touching the file
/// system, and so the storage backend (file, vault, db) can evolve without
/// rippling through callers (Dependency Inversion Principle).
/// </summary>
public interface IStravaTokenStore
{
    /// <summary>
    /// Reads the currently persisted token set, if any.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel I/O.</param>
    /// <returns>The persisted tokens, or null when no consent has been recorded yet.</returns>
    Task<StravaTokenSet?> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically replaces the persisted token set.
    /// </summary>
    /// <param name="tokens">The new token set to persist.</param>
    /// <param name="cancellationToken">Token used to cancel I/O.</param>
    Task SaveAsync(StravaTokenSet tokens, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the persisted token set, effectively disconnecting the account.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel I/O.</param>
    Task DeleteAsync(CancellationToken cancellationToken = default);
}
