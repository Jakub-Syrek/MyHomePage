namespace MyHomePage.Tests.Services;

/// <summary>
/// Tests for the password-reset surface added to
/// <see cref="CredentialService"/> — covers all three outcomes
/// (<see cref="PasswordResetOutcome.Updated"/>,
/// <see cref="PasswordResetOutcome.ManualUpdateRequired"/>,
/// <see cref="PasswordResetOutcome.AccountNotFound"/>) and the
/// <c>HasAccount</c> helper that gates the flow.
/// </summary>
[TestFixture]
public sealed class CredentialServiceResetTests
{
    private string _tempContentRoot = null!;
    private CredentialService _service = null!;
    private string? _origAdminUsers;
    private string? _origAdminEmail;
    private string? _origAdminPassword;

    [SetUp]
    public void Setup()
    {
        _tempContentRoot = Directory.CreateTempSubdirectory("cred-reset-").FullName;
        _origAdminUsers = Environment.GetEnvironmentVariable("ADMIN_USERS");
        _origAdminEmail = Environment.GetEnvironmentVariable("ADMIN_EMAIL");
        _origAdminPassword = Environment.GetEnvironmentVariable("ADMIN_PASSWORD");
        Environment.SetEnvironmentVariable("ADMIN_USERS", null);
        Environment.SetEnvironmentVariable("ADMIN_EMAIL", null);
        Environment.SetEnvironmentVariable("ADMIN_PASSWORD", null);

        var env = Substitute.For<IWebHostEnvironment>();
        env.ContentRootPath.Returns(_tempContentRoot);
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

    [Test]
    public void HasAccount_NoSourcesConfigured_ReturnsFalse()
    {
        Assert.That(_service.HasAccount("anyone@x.com"), Is.False);
    }

    [Test]
    public void HasAccount_AdminUsersEnvVar_FindsMatchingEmailCaseInsensitively()
    {
        Environment.SetEnvironmentVariable("ADMIN_USERS", """
        [ { "email": "User@Example.com", "password": "pw" } ]
        """);

        Assert.That(_service.HasAccount("user@example.com"), Is.True);
        Assert.That(_service.HasAccount("nobody@example.com"), Is.False);
    }

    [Test]
    public void HasAccount_AdminEmailEnvVar_FindsMatch()
    {
        Environment.SetEnvironmentVariable("ADMIN_EMAIL", "owner@x.com");
        Environment.SetEnvironmentVariable("ADMIN_PASSWORD", "secret");

        Assert.That(_service.HasAccount("owner@x.com"), Is.True);
        Assert.That(_service.HasAccount("Owner@X.com"), Is.True);
        Assert.That(_service.HasAccount("other@x.com"), Is.False);
    }

    [Test]
    public void HasAccount_CredentialsFile_FindsMatch()
    {
        WriteCredentialsFile("""
        { "users": [ { "email": "f@x.com", "password": "pw" } ] }
        """);

        Assert.That(_service.HasAccount("f@x.com"), Is.True);
        Assert.That(_service.HasAccount("g@x.com"), Is.False);
    }

    [Test]
    public async Task ResetPasswordAsync_UnknownAccount_ReturnsAccountNotFound()
    {
        var result = await _service.ResetPasswordAsync("nobody@x.com", "newpass");

        Assert.That(result.Outcome, Is.EqualTo(PasswordResetOutcome.AccountNotFound));
        Assert.That(result.NewBCryptHash, Is.Null);
    }

    [Test]
    public async Task ResetPasswordAsync_EnvVarBacked_ReturnsManualUpdateWithHash()
    {
        Environment.SetEnvironmentVariable("ADMIN_EMAIL", "owner@x.com");
        Environment.SetEnvironmentVariable("ADMIN_PASSWORD", "old");

        var result = await _service.ResetPasswordAsync("owner@x.com", "newSecret123");

        Assert.That(result.Outcome, Is.EqualTo(PasswordResetOutcome.ManualUpdateRequired));
        Assert.That(result.NewBCryptHash, Is.Not.Null);
        Assert.That(result.NewBCryptHash, Does.StartWith("$2"));
        // The new hash should verify against the supplied plaintext.
        Assert.That(BCrypt.Net.BCrypt.Verify("newSecret123", result.NewBCryptHash), Is.True);
    }

    [Test]
    public async Task ResetPasswordAsync_FileBacked_RewritesCredentialsAndPasswordVerifies()
    {
        WriteCredentialsFile("""
        { "users": [
            { "email": "alice@x.com", "password": "old-alice" },
            { "email": "bob@x.com",   "password": "old-bob"   }
        ] }
        """);

        var result = await _service.ResetPasswordAsync("alice@x.com", "newAlicePass!");

        Assert.That(result.Outcome, Is.EqualTo(PasswordResetOutcome.Updated));
        Assert.That(result.NewBCryptHash, Is.Null);

        // New password works; old password no longer does.
        Assert.That(_service.ValidateCredentials("alice@x.com", "newAlicePass!"), Is.True);
        Assert.That(_service.ValidateCredentials("alice@x.com", "old-alice"), Is.False);

        // Bob's record is untouched.
        Assert.That(_service.ValidateCredentials("bob@x.com", "old-bob"), Is.True);
    }

    private void WriteCredentialsFile(string json) =>
        File.WriteAllText(Path.Combine(_tempContentRoot, "credentials.json"), json);
}
