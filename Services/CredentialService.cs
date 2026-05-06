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

    public CredentialService(IWebHostEnvironment environment)
    {
        _credentialsPath = Path.Combine(environment.ContentRootPath, "credentials.json");
    }

    public bool ValidateCredentials(string email, string password)
    {
        try
        {
            if (!File.Exists(_credentialsPath))
                return false;

            var json = File.ReadAllText(_credentialsPath);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("users", out var usersArray))
                return false;

            foreach (var user in usersArray.EnumerateArray())
            {
                if (user.TryGetProperty("email", out var emailEl) &&
                    user.TryGetProperty("password", out var passwordEl) &&
                    emailEl.GetString() == email &&
                    passwordEl.GetString() == password)
                {
                    return true;
                }
            }
        }
        catch
        {
            return false;
        }

        return false;
    }
}
