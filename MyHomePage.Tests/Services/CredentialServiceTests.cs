namespace MyHomePage.Tests.Services;

[TestFixture]
public class CredentialServiceTests
{
    private CredentialService _service = null!;
    private string _tempDirectory = null!;

    [SetUp]
    public void Setup()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"CredentialTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDirectory);

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
    public void ValidateCredentials_FileDoesNotExist_ReturnsFalse()
    {
        // Act
        var result = _service.ValidateCredentials("admin@example.com", "password123");

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void ValidateCredentials_WithValidCredentialsFile_ValidatesCorrectly()
    {
        // Arrange
        var credentialsPath = Path.Combine(_tempDirectory, "credentials.json");
        var json = """
            {
                "users": [
                    {
                        "email": "test@example.com",
                        "password": "testpass"
                    }
                ]
            }
            """;
        File.WriteAllText(credentialsPath, json);

        // Act
        var resultValid = _service.ValidateCredentials("test@example.com", "testpass");
        var resultInvalid = _service.ValidateCredentials("test@example.com", "wrongpass");

        // Assert
        Assert.That(resultValid, Is.True);
        Assert.That(resultInvalid, Is.False);
    }
}
