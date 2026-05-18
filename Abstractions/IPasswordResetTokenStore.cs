namespace MyHomePage.Abstractions;

/// <summary>
/// A single password-reset token record. The plaintext token is only
/// ever held in memory for the few milliseconds between generation and
/// emailing — what we persist is its SHA-256 hash so a leaked file
/// can't be turned into a working reset link.
/// </summary>
/// <param name="TokenHash">Hex-encoded SHA-256 of the plaintext token.</param>
/// <param name="Email">Account the token was issued for (case-folded).</param>
/// <param name="CreatedUtc">Timestamp the token was issued.</param>
/// <param name="ExpiresUtc">UTC instant after which the token is invalid.</param>
/// <param name="UsedUtc">UTC instant when the token was consumed, or null.</param>
public sealed record PasswordResetToken(
    string TokenHash,
    string Email,
    DateTime CreatedUtc,
    DateTime ExpiresUtc,
    DateTime? UsedUtc);

/// <summary>
/// Persistence boundary for password-reset tokens. Abstracted so the
/// reset pages can be unit-tested without touching the file system and
/// so a future swap to a real database doesn't ripple through callers.
/// </summary>
public interface IPasswordResetTokenStore
{
    /// <summary>
    /// Stores a freshly issued token. Implementations may purge
    /// expired entries opportunistically to keep the file small.
    /// </summary>
    Task SaveAsync(PasswordResetToken token, CancellationToken cancellationToken = default);

    /// <summary>
    /// Looks up an unexpired, unused token by its hash. Returns
    /// <c>null</c> when not found, already used, or past
    /// <see cref="PasswordResetToken.ExpiresUtc"/>.
    /// </summary>
    Task<PasswordResetToken?> FindActiveAsync(string tokenHash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the supplied token as used so it cannot be replayed.
    /// </summary>
    Task MarkUsedAsync(string tokenHash, CancellationToken cancellationToken = default);
}
