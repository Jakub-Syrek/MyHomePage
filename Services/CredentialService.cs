using System.Text.Json;

namespace MyHomePage.Services;

public class CredentialService
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
            var root = doc.RootElement;

            if (root.TryGetProperty("users", out var usersArray))
            {
                foreach (var user in usersArray.EnumerateArray())
                {
                    if (user.TryGetProperty("email", out var emailElement) &&
                        user.TryGetProperty("password", out var passwordElement))
                    {
                        var storedEmail = emailElement.GetString();
                        var storedPassword = passwordElement.GetString();

                        if (storedEmail == email && storedPassword == password)
                        {
                            return true;
                        }
                    }
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
