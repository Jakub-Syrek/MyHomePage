namespace MyHomePage.Tests.Services;

[TestFixture]
public class CredentialServiceTests
{
    private CredentialService _service = null!;
    private string _tempDirectory = null!;
    private string _credentialsPath = null!;

    [SetUp]
    public void Setup()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"CredentialTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDirectory);
        _credentialsPath = Path.Combine(_tempDirectory, "credentials.json");

        var mockEnvironment = Substitute.For<IWebHostEnvironment>();
        mockEnvironment.ContentRootPath.Returns(_tempDirectory);
        var mockLogger = Substitute.For<ILogger<CredentialService>>();
        _service = new CredentialService(mockEnvironment, mockLogger);
    }

    [TearDown]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDirectory))
            Directory.Delete(_tempDirectory, recursive: true);
    }

    [Test]
    public void ValidateCredentials_CorrectEmailAndPassword_ReturnsTrue()
    {
        // Arrange
        var credentials = new
        {
            Users = new[] {
                new { Email = "admin@example.com", Password = "password123" }
            }
        };
        var json = System.Text.Json.JsonSerializer.Serialize(credentials);
        File.WriteAllText(_credentialsPath, json);

        // Act
        var result = _service.ValidateCredentials("admin@example.com", "password123");

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void ValidateCredentials_WrongPassword_ReturnsFalse()
    {
        // Arrange
        var credentials = new
        {
            Users = new[] {
                new { Email = "admin@example.com", Password = "password123" }
            }
        };
        var json = System.Text.Json.JsonSerializer.Serialize(credentials);
        File.WriteAllText(_credentialsPath, json);

        // Act
        var result = _service.ValidateCredentials("admin@example.com", "wrongpassword");

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void ValidateCredentials_FileDoesNotExist_ReturnsFalse()
    {
        // Act
        var result = _service.ValidateCredentials("admin@example.com", "password123");

        // Assert
        Assert.That(result, Is.False);
    }
}
