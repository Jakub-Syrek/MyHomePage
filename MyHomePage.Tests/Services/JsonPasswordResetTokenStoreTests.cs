namespace MyHomePage.Tests.Services;

/// <summary>
/// Tests for <see cref="JsonPasswordResetTokenStore"/> — exercises the
/// real filesystem against a temp directory so save / find / mark-used
/// are end-to-end. Mirrors the pattern used by
/// <see cref="JsonStravaTokenStoreTests"/>.
/// </summary>
[TestFixture]
public sealed class JsonPasswordResetTokenStoreTests
{
    private string _tempRoot = null!;
    private IFileStorageService _storage = null!;
    private JsonPasswordResetTokenStore _store = null!;

    [SetUp]
    public void Setup()
    {
        _tempRoot = Directory.CreateTempSubdirectory("pw-reset-tokens-").FullName;
        _storage = Substitute.For<IFileStorageService>();
        _storage.GetVideosRootPath().Returns(_tempRoot);

        var logger = Substitute.For<ILogger<JsonPasswordResetTokenStore>>();
        _store = new JsonPasswordResetTokenStore(_storage, logger);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    [Test]
    public async Task FindActiveAsync_NoFile_ReturnsNull()
    {
        var found = await _store.FindActiveAsync("anything");

        Assert.That(found, Is.Null);
    }

    [Test]
    public async Task SaveThenFindActive_RoundTrips()
    {
        var token = new PasswordResetToken(
            TokenHash: "ABCD1234",
            Email: "user@example.com",
            CreatedUtc: DateTime.UtcNow,
            ExpiresUtc: DateTime.UtcNow.AddHours(1),
            UsedUtc: null);

        await _store.SaveAsync(token);
        var found = await _store.FindActiveAsync("ABCD1234");

        Assert.That(found, Is.Not.Null);
        Assert.That(found!.Email, Is.EqualTo("user@example.com"));
        Assert.That(found.UsedUtc, Is.Null);
    }

    [Test]
    public async Task FindActiveAsync_ExpiredToken_ReturnsNull()
    {
        var expired = new PasswordResetToken(
            TokenHash: "EXPIRED",
            Email: "u@x.com",
            CreatedUtc: DateTime.UtcNow.AddHours(-2),
            ExpiresUtc: DateTime.UtcNow.AddMinutes(-1),
            UsedUtc: null);
        await _store.SaveAsync(expired);

        var found = await _store.FindActiveAsync("EXPIRED");

        Assert.That(found, Is.Null);
    }

    [Test]
    public async Task FindActiveAsync_UsedToken_ReturnsNull()
    {
        var used = new PasswordResetToken(
            TokenHash: "USED",
            Email: "u@x.com",
            CreatedUtc: DateTime.UtcNow,
            ExpiresUtc: DateTime.UtcNow.AddHours(1),
            UsedUtc: DateTime.UtcNow);
        await _store.SaveAsync(used);

        var found = await _store.FindActiveAsync("USED");

        Assert.That(found, Is.Null);
    }

    [Test]
    public async Task MarkUsedAsync_PreventsFurtherFinds()
    {
        var token = new PasswordResetToken(
            TokenHash: "ONCE",
            Email: "u@x.com",
            CreatedUtc: DateTime.UtcNow,
            ExpiresUtc: DateTime.UtcNow.AddHours(1),
            UsedUtc: null);
        await _store.SaveAsync(token);

        await _store.MarkUsedAsync("ONCE");
        var found = await _store.FindActiveAsync("ONCE");

        Assert.That(found, Is.Null);
    }

    [Test]
    public async Task MarkUsedAsync_UnknownHash_IsSilentNoOp()
    {
        Assert.DoesNotThrowAsync(async () => await _store.MarkUsedAsync("DOES-NOT-EXIST"));
    }

    [Test]
    public async Task SaveAsync_OldExpiredTokensArePurged()
    {
        var ancient = new PasswordResetToken(
            TokenHash: "ANCIENT",
            Email: "old@x.com",
            CreatedUtc: DateTime.UtcNow.AddDays(-10),
            ExpiresUtc: DateTime.UtcNow.AddDays(-9), // > 24h past expiry
            UsedUtc: null);
        await _store.SaveAsync(ancient);

        var fresh = new PasswordResetToken(
            TokenHash: "FRESH",
            Email: "new@x.com",
            CreatedUtc: DateTime.UtcNow,
            ExpiresUtc: DateTime.UtcNow.AddHours(1),
            UsedUtc: null);
        await _store.SaveAsync(fresh);

        // Ancient should have been cleaned up; fresh remains.
        // FindActiveAsync on a not-yet-but-just-passed-expiry would also return
        // null, but the purge specifically removes the entry from disk — we
        // can verify by re-loading via FindActiveAsync on an "extended" entry.
        var rawJson = await File.ReadAllTextAsync(Path.Combine(_tempRoot, "password-reset-tokens.json"));
        Assert.That(rawJson, Does.Not.Contain("ANCIENT"));
        Assert.That(rawJson, Does.Contain("FRESH"));
    }

    [Test]
    public async Task LoadAsync_CorruptFile_StartsFresh()
    {
        await File.WriteAllTextAsync(
            Path.Combine(_tempRoot, "password-reset-tokens.json"),
            "{ not valid json");

        // Save still works (overwrites the corrupt file).
        var token = new PasswordResetToken(
            TokenHash: "AFTER-CORRUPT",
            Email: "u@x.com",
            CreatedUtc: DateTime.UtcNow,
            ExpiresUtc: DateTime.UtcNow.AddHours(1),
            UsedUtc: null);

        Assert.DoesNotThrowAsync(async () => await _store.SaveAsync(token));
        var found = await _store.FindActiveAsync("AFTER-CORRUPT");
        Assert.That(found, Is.Not.Null);
    }
}
