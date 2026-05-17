namespace MyHomePage.Tests.Services;

/// <summary>
/// Tests for <see cref="CredentialService"/> covering every resolution
/// tier (ADMIN_USERS array, ADMIN_EMAIL + ADMIN_PASSWORD pair, JSON file)
/// and the failure paths in between. Environment variables are saved and
/// restored per-test to keep the suite hermetic.
/// </summary>
[TestFixture]
public sealed class CredentialServiceFullTests
{
    private string _tempContentRoot = null!;
    private CredentialService _service = null!;
    private string? _origAdminUsers;
    private string? _origAdminEmail;
    private string? _origAdminPassword;

    [SetUp]
    public void Setup()
    {
        _tempContentRoot = Directory.CreateTempSubdirectory("cred-tests-").FullName;
        _origAdminUsers = Environment.GetEnvironmentVariable("ADMIN_USERS");
        _origAdminEmail = Environment.GetEnvironmentVariable("ADMIN_EMAIL");
        _origAdminPassword = Environment.GetEnvironmentVariable("ADMIN_PASSWORD");
        Environment.SetEnvironmentVariable("ADMIN_USERS", null);
        Environment.SetEnvironmentVariable("ADMIN_EMAIL", null);
        Environment.SetEnvironmentVariable("ADMIN_PASSWORD", null);

        var env = new FakeEnv { ContentRootPath = _tempContentRoot };
        var logger = Substitute.For<ILogger<CredentialService>>();
        _service = new CredentialService(env, logger);
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable("ADMIN_USERS", _origAdminUsers);
        Environment.SetEnvironmentVariable("ADMIN_EMAIL", _origAdminEmail);
        Environment.SetEnvironmentVariable("ADMIN_PASSWORD", _origAdminPassword);
        try { if (Directory.Exists(_tempContentRoot)) Directory.Delete(_tempContentRoot, true); }
        catch { }
    }

    // ── ADMIN_USERS env var ───────────────────────────────────────────────

    [Test]
    public void ValidateCredentials_AdminUsersJsonArray_MatchingEntry_ReturnsTrue()
    {
        Environment.SetEnvironmentVariable("ADMIN_USERS", """
        [
            { "email": "first@x.com", "password": "p1" },
            { "email": "second@x.com", "password": "p2" }
        ]
        """);

        Assert.That(_service.ValidateCredentials("second@x.com", "p2"), Is.True);
    }

    [Test]
    public void ValidateCredentials_AdminUsersJsonArray_CaseInsensitiveEmail()
    {
        Environment.SetEnvironmentVariable("ADMIN_USERS", """
        [ { "email": "User@Example.com", "password": "pw" } ]
        """);

        Assert.That(_service.ValidateCredentials("user@example.com", "pw"), Is.True);
    }

    [Test]
    public void ValidateCredentials_AdminUsersJsonArray_NoMatch_ReturnsFalse()
    {
        Environment.SetEnvironmentVariable("ADMIN_USERS", """
        [ { "email": "u@x.com", "password": "right" } ]
        """);

        Assert.That(_service.ValidateCredentials("u@x.com", "wrong"), Is.False);
    }

    [Test]
    public void ValidateCredentials_AdminUsersJsonArray_NotAnArray_ReturnsFalse()
    {
        Environment.SetEnvironmentVariable("ADMIN_USERS", """
        { "users": [ { "email": "u@x.com", "password": "p" } ] }
        """);

        Assert.That(_service.ValidateCredentials("u@x.com", "p"), Is.False);
    }

    [Test]
    public void ValidateCredentials_AdminUsersMalformedJson_ReturnsFalse()
    {
        Environment.SetEnvironmentVariable("ADMIN_USERS", "{ not valid");

        Assert.That(_service.ValidateCredentials("u@x.com", "p"), Is.False);
    }

    // ── ADMIN_EMAIL + ADMIN_PASSWORD env vars ─────────────────────────────

    [Test]
    public void ValidateCredentials_AdminEmailPair_Match_ReturnsTrue()
    {
        Environment.SetEnvironmentVariable("ADMIN_EMAIL", "owner@x.com");
        Environment.SetEnvironmentVariable("ADMIN_PASSWORD", "secret");

        Assert.That(_service.ValidateCredentials("owner@x.com", "secret"), Is.True);
    }

    [Test]
    public void ValidateCredentials_AdminEmailPair_CaseInsensitiveEmail()
    {
        Environment.SetEnvironmentVariable("ADMIN_EMAIL", "Admin@X.com");
        Environment.SetEnvironmentVariable("ADMIN_PASSWORD", "secret");

        Assert.That(_service.ValidateCredentials("admin@x.com", "secret"), Is.True);
    }

    [Test]
    public void ValidateCredentials_AdminEmailPair_WrongPassword_ReturnsFalse()
    {
        Environment.SetEnvironmentVariable("ADMIN_EMAIL", "owner@x.com");
        Environment.SetEnvironmentVariable("ADMIN_PASSWORD", "secret");

        Assert.That(_service.ValidateCredentials("owner@x.com", "other"), Is.False);
    }

    // ── credentials.json file ─────────────────────────────────────────────

    [Test]
    public void ValidateCredentials_FileWithUsersObject_MatchingEntry_ReturnsTrue()
    {
        WriteCredentialsFile("""
        { "users": [
            { "email": "f@x.com", "password": "pw" }
        ] }
        """);

        Assert.That(_service.ValidateCredentials("f@x.com", "pw"), Is.True);
    }

    [Test]
    public void ValidateCredentials_FileMissingUsersProperty_ReturnsFalse()
    {
        WriteCredentialsFile("""{ "other": [] }""");

        Assert.That(_service.ValidateCredentials("f@x.com", "pw"), Is.False);
    }

    [Test]
    public void ValidateCredentials_FileMalformedJson_ReturnsFalse()
    {
        WriteCredentialsFile("{ not valid");

        Assert.That(_service.ValidateCredentials("f@x.com", "pw"), Is.False);
    }

    [Test]
    public void ValidateCredentials_FileUserMissingPasswordProperty_Skipped()
    {
        WriteCredentialsFile("""
        { "users": [
            { "email": "incomplete@x.com" },
            { "email": "complete@x.com", "password": "good" }
        ] }
        """);

        Assert.That(_service.ValidateCredentials("incomplete@x.com", ""), Is.False);
        Assert.That(_service.ValidateCredentials("complete@x.com", "good"), Is.True);
    }

    private void WriteCredentialsFile(string json) =>
        File.WriteAllText(Path.Combine(_tempContentRoot, "credentials.json"), json);

    private sealed class FakeEnv : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = string.Empty;
        public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = null!;
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = string.Empty;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
        public string EnvironmentName { get; set; } = "Testing";
    }
}
