namespace MyHomePage.Abstractions;

/// <summary>
/// Abstraction for credential validation.
/// Decouples authentication logic from the concrete storage mechanism
/// (Dependency Inversion Principle — D in SOLID).
/// </summary>
public interface ICredentialRepository
{
    bool ValidateCredentials(string email, string password);
}
