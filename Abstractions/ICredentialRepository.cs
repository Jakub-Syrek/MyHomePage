namespace MyHomePage.Abstractions;

/// <summary>
/// Outcome of a password-reset attempt. Lets the caller distinguish
/// between a clean write, a soft failure (account was issued through
/// env vars, so the operator has to update Railway manually) and a
/// hard failure (no such account).
/// </summary>
public enum PasswordResetOutcome
{
    /// <summary>The new hash was persisted to the credentials store.</summary>
    Updated,

    /// <summary>The account exists, but the credential lives in an env
    /// var Claude can't rewrite — the caller gets the new hash to paste
    /// into the platform's secret manager.</summary>
    ManualUpdateRequired,

    /// <summary>No account with that email — surface as a generic
    /// "if the account exists you'll get an email" so we don't leak
    /// whether the address is in our database.</summary>
    AccountNotFound
}

/// <summary>
/// Result of a password-reset call. <see cref="NewBCryptHash"/> is
/// non-null when the operator needs it (i.e. when the secret is held
/// in env vars and only they can rotate it).
/// </summary>
/// <param name="Outcome">Categorised result of the reset.</param>
/// <param name="NewBCryptHash">Non-null BCrypt hash to be applied
/// manually when <see cref="PasswordResetOutcome.ManualUpdateRequired"/>.</param>
public sealed record PasswordResetResult(PasswordResetOutcome Outcome, string? NewBCryptHash);

/// <summary>
/// Abstraction for credential validation.
/// Decouples authentication logic from the concrete storage mechanism
/// (Dependency Inversion Principle — D in SOLID).
/// </summary>
public interface ICredentialRepository
{
    /// <summary>Returns true when the supplied password matches the
    /// stored credential for <paramref name="email"/>.</summary>
    bool ValidateCredentials(string email, string password);

    /// <summary>Returns true when an account is configured for the
    /// supplied email — used by the password-reset flow to decide
    /// whether to issue a token without leaking the answer back to
    /// the caller.</summary>
    bool HasAccount(string email);

    /// <summary>Replaces the stored secret for <paramref name="email"/>
    /// with a fresh BCrypt hash of <paramref name="newPassword"/>.</summary>
    Task<PasswordResetResult> ResetPasswordAsync(
        string email, string newPassword, CancellationToken cancellationToken = default);
}
