using System.Text.Json;
using MyHomePage.Abstractions;

namespace MyHomePage.Services;

/// <summary>
/// Validates user credentials stored in credentials.json.
/// Implements ICredentialRepository so callers depend on the abstraction,
/// not the concrete file-based storage (Dependency Inversion Principle).
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
        _logger.LogDebug("ValidateCredentials called for email: {Email}, credentials file: {Path}",
            email, _credentialsPath);

        try
        {
            if (!File.Exists(_credentialsPath))
            {
                _logger.LogError("Credentials file not found at {Path}", _credentialsPath);
                return false;
            }

            _logger.LogDebug("Reading credentials from {Path}", _credentialsPath);
            var json = File.ReadAllText(_credentialsPath);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("users", out var usersArray))
            {
                _logger.LogError("'users' property not found in credentials.json");
                return false;
            }

            var userCount = 0;
            foreach (var user in usersArray.EnumerateArray())
            {
                userCount++;
                if (user.TryGetProperty("email", out var emailEl) &&
                    user.TryGetProperty("password", out var passwordEl))
                {
                    var storedEmail = emailEl.GetString();
                    var storedPassword = passwordEl.GetString();

                    if (storedEmail == email && storedPassword == password)
                    {
                        _logger.LogInformation("Credentials validated successfully for {Email}", email);
                        return true;
                    }
                }
            }

            _logger.LogWarning("Credentials validation failed for {Email}. Checked {UserCount} users in credentials file",
                email, userCount);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during credential validation for {Email}", email);
            return false;
        }
    }
}
