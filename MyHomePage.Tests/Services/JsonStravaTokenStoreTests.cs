namespace MyHomePage.Tests.Services;

/// <summary>
/// Tests for <see cref="JsonStravaTokenStore"/> — exercises the real
/// filesystem against a temp directory so save/load/delete are end-to-end.
/// </summary>
[TestFixture]
public sealed class JsonStravaTokenStoreTests
{
    private string _tempRoot = null!;
    private IFileStorageService _storage = null!;
    private JsonStravaTokenStore _store = null!;

    [SetUp]
    public void Setup()
    {
        _tempRoot = Directory.CreateTempSubdirectory("strava-tokens-").FullName;
        _storage = Substitute.For<IFileStorageService>();
        _storage.GetVideosRootPath().Returns(_tempRoot);

        var logger = Substitute.For<ILogger<JsonStravaTokenStore>>();
        _store = new JsonStravaTokenStore(_storage, logger);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    [Test]
    public async Task LoadAsync_NoFileOnDisk_ReturnsNull()
    {
        var tokens = await _store.LoadAsync();

        Assert.That(tokens, Is.Null);
    }

    [Test]
    public async Task SaveAsync_PersistsTokensAndLoadRoundTrips()
    {
        var original = new StravaTokenSet
        {
            AthleteId = 42,
            AccessToken = "access-xyz",
            RefreshToken = "refresh-abc",
            ExpiresAtUtc = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc),
            Scope = "read,activity:read_all"
        };

        await _store.SaveAsync(original);
        var roundTripped = await _store.LoadAsync();

        Assert.That(roundTripped, Is.Not.Null);
        Assert.That(roundTripped!.AthleteId, Is.EqualTo(42));
        Assert.That(roundTripped.AccessToken, Is.EqualTo("access-xyz"));
        Assert.That(roundTripped.RefreshToken, Is.EqualTo("refresh-abc"));
        Assert.That(roundTripped.ExpiresAtUtc, Is.EqualTo(original.ExpiresAtUtc));
        Assert.That(roundTripped.Scope, Is.EqualTo("read,activity:read_all"));
    }

    [Test]
    public async Task SaveAsync_OverwritesPreviousFile()
    {
        await _store.SaveAsync(new StravaTokenSet
        {
            AthleteId = 1,
            AccessToken = "first",
            RefreshToken = "r1",
            ExpiresAtUtc = DateTime.UtcNow.AddHours(1),
            Scope = "read"
        });

        await _store.SaveAsync(new StravaTokenSet
        {
            AthleteId = 2,
            AccessToken = "second",
            RefreshToken = "r2",
            ExpiresAtUtc = DateTime.UtcNow.AddHours(2),
            Scope = "activity:read_all"
        });

        var current = await _store.LoadAsync();

        Assert.That(current, Is.Not.Null);
        Assert.That(current!.AthleteId, Is.EqualTo(2));
        Assert.That(current.AccessToken, Is.EqualTo("second"));
    }

    [Test]
    public async Task DeleteAsync_RemovesTheFile()
    {
        await _store.SaveAsync(new StravaTokenSet
        {
            AthleteId = 7,
            AccessToken = "to-be-deleted",
            RefreshToken = "rt",
            ExpiresAtUtc = DateTime.UtcNow.AddHours(1),
            Scope = "read"
        });

        await _store.DeleteAsync();
        var afterDelete = await _store.LoadAsync();

        Assert.That(afterDelete, Is.Null);
        Assert.That(Directory.GetFiles(_tempRoot, "strava-tokens.json"), Is.Empty);
    }

    [Test]
    public async Task DeleteAsync_NoFile_IsSilentNoOp()
    {
        // No SaveAsync first.

        Assert.DoesNotThrowAsync(async () => await _store.DeleteAsync());
    }

    [Test]
    public async Task SaveAsync_CreatesMissingRootDirectory()
    {
        var nestedRoot = Path.Combine(_tempRoot, "deep", "nested", "root");
        _storage.GetVideosRootPath().Returns(nestedRoot);

        await _store.SaveAsync(new StravaTokenSet
        {
            AthleteId = 11,
            AccessToken = "a",
            RefreshToken = "r",
            ExpiresAtUtc = DateTime.UtcNow.AddHours(1),
            Scope = "read"
        });

        Assert.That(Directory.Exists(nestedRoot), Is.True);
        Assert.That(File.Exists(Path.Combine(nestedRoot, "strava-tokens.json")), Is.True);
    }

    [Test]
    public async Task LoadAsync_CorruptJson_ReturnsNullInsteadOfThrowing()
    {
        await File.WriteAllTextAsync(
            Path.Combine(_tempRoot, "strava-tokens.json"),
            "{ this is not valid json");

        var tokens = await _store.LoadAsync();

        Assert.That(tokens, Is.Null);
    }
}
