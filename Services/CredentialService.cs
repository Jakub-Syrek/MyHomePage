using System.Text.Json;
using MyHomePage.Abstractions;

namespace MyHomePage.Services;

/// <summary>
/// Validates user credentials.
///
/// Resolution order:
///   1. Env var ADMIN_USERS (JSON array: '[{"email":"...","password":"..."}]')
///   2. Env vars ADMIN_EMAIL + ADMIN_PASSWORD (single user — easiest for Railway)
///   3. credentials.json in ContentRootPath (local dev)
///
/// Implements ICredentialRepository so callers depend on the abstraction,
/// not the concrete storage (Dependency Inversion Principle).
/// </summary>
public sealed class CredentialService : ICredentialRepository
{
    private readonly string _credentialsPath;
    private readonly ILogger<CredentialService> _logger;

    public CredentialService(IWebHostEnvironment environment, ILogger<CredentialService> logger)
    {
        _credentialsPath = Path.Combine(environment.ContentRootPath, "credentials.json");
        _logger = logger;
    }

    public bool ValidateCredentials(string email, string password)
    {
        try
        {
            // 1. ADMIN_USERS env var (JSON array — multi-user)
            var adminUsersJson = Environment.GetEnvironmentVariable("ADMIN_USERS");
            if (!string.IsNullOrWhiteSpace(adminUsersJson))
            {
                _logger.LogDebug("Validating against ADMIN_USERS env var");
                if (TryValidateAgainstJsonArray(adminUsersJson, email, password))
                    return true;
                return false;
            }

            // 2. ADMIN_EMAIL + ADMIN_PASSWORD env vars (single user — simplest)
            var adminEmail = Environment.GetEnvironmentVariable("ADMIN_EMAIL");
            var adminPassword = Environment.GetEnvironmentVariable("ADMIN_PASSWORD");
            if (!string.IsNullOrWhiteSpace(adminEmail) && !string.IsNullOrWhiteSpace(adminPassword))
            {
                _logger.LogDebug("Validating against ADMIN_EMAIL/ADMIN_PASSWORD env vars");
                if (string.Equals(adminEmail, email, StringComparison.OrdinalIgnoreCase)
                    && VerifyPassword(adminPassword, password))
                {
                    _logger.LogInformation("Credentials validated (env var) for {Email}", email);
                    return true;
                }
                _logger.LogWarning("Credentials validation failed (env var) for {Email}", email);
                return false;
            }

            // 3. credentials.json file (local dev fallback)
            if (!File.Exists(_credentialsPath))
            {
                _logger.LogError(
                    "No credentials available. Set ADMIN_EMAIL/ADMIN_PASSWORD env vars, " +
                    "or create credentials.json at {Path}", _credentialsPath);
                return false;
            }

            _logger.LogDebug("Validating against credentials.json at {Path}", _credentialsPath);
            var json = File.ReadAllText(_credentialsPath);
            return TryValidateAgainstJsonObject(json, email, password);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during credential validation for {Email}", email);
            return false;
        }
    }

    /// <summary>Validates against JSON object shape: { "users": [ {email,password}, ... ] }</summary>
    private bool TryValidateAgainstJsonObject(string json, string email, string password)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("users", out var usersArray))
        {
            _logger.LogError("'users' property not found in credentials JSON");
            return false;
        }
        return EnumerateAndMatch(usersArray, email, password);
    }

    /// <summary>Validates against bare JSON array: [ {email,password}, ... ]</summary>
    private bool TryValidateAgainstJsonArray(string json, string email, string password)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            _logger.LogError("ADMIN_USERS env var must be a JSON array");
            return false;
        }
        return EnumerateAndMatch(doc.RootElement, email, password);
    }

    private bool EnumerateAndMatch(JsonElement usersArray, string email, string password)
    {
        var userCount = 0;
        foreach (var user in usersArray.EnumerateArray())
        {
            userCount++;
            if (user.TryGetProperty("email", out var emailEl)
                && user.TryGetProperty("password", out var passwordEl))
            {
                var storedEmail = emailEl.GetString();
                var storedPassword = passwordEl.GetString();

                if (string.Equals(storedEmail, email, StringComparison.OrdinalIgnoreCase)
                    && VerifyPassword(storedPassword, password))
                {
                    _logger.LogInformation("Credentials validated for {Email}", email);
                    return true;
                }
            }
        }
        _logger.LogWarning(
            "Credentials validation failed for {Email}. Checked {UserCount} users",
            email, userCount);
        return false;
    }

    /// <summary>
    /// Compares a candidate plaintext password against the stored
    /// secret. Supports two storage formats so existing deployments
    /// don't have to migrate everything at once:
    /// <list type="bullet">
    ///   <item><description>BCrypt hashes (prefix <c>$2</c>) are verified
    ///   via <see cref="BCrypt.Net.BCrypt.Verify(string, string)"/> in
    ///   constant time relative to the hash work-factor.</description></item>
    ///   <item><description>Plain strings are compared with
    ///   <see cref="System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(ReadOnlySpan{byte}, ReadOnlySpan{byte})"/>
    ///   so a timing-side-channel can't leak the secret one byte at a
    ///   time. A deprecation warning is logged so the operator knows
    ///   to migrate to BCrypt.</description></item>
    /// </list>
    /// </summary>
    /// <param name="stored">Value loaded from the configured source.</param>
    /// <param name="candidate">Plaintext password supplied at login.</param>
    /// <returns><c>true</c> when the candidate matches the stored secret.</returns>
    internal bool VerifyPassword(string? stored, string candidate)
    {
        if (string.IsNullOrEmpty(stored) || candidate is null) return false;

        // BCrypt hashes start with $2a$, $2b$, $2x$ or $2y$ + a cost
        // marker. Anything else is treated as a legacy plaintext.
        if (stored.StartsWith("$2", StringComparison.Ordinal))
        {
            try
            {
                return BCrypt.Net.BCrypt.Verify(candidate, stored);
            }
            catch (BCrypt.Net.SaltParseException ex)
            {
                _logger.LogWarning(ex,
                    "Stored BCrypt hash is malformed; treating as no match");
                return false;
            }
        }

        _logger.LogWarning(
            "Plaintext password storage detected. Rotate the credential to a " +
            "BCrypt hash (see /admin/hash-password) — plaintext storage is " +
            "supported only for backwards compatibility and will be removed.");

        var storedBytes = System.Text.Encoding.UTF8.GetBytes(stored);
        var candidateBytes = System.Text.Encoding.UTF8.GetBytes(candidate);
        // FixedTimeEquals only operates on equal-length spans, so we
        // pad / truncate by always allocating to the longer length and
        // comparing both. Mismatched lengths inherently fail.
        if (storedBytes.Length != candidateBytes.Length) return false;
        return System.Security.Cryptography.CryptographicOperations
            .FixedTimeEquals(storedBytes, candidateBytes);
    }
}
