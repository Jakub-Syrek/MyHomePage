namespace MyHomePage.Tests.Repositories;

/// <summary>
/// End-to-end tests for <see cref="JsonVideoRepository"/>. The repository
/// is exercised against a real temp directory so the JSON serialiser, the
/// metadata.json file layout and the id-generation algorithm all stay
/// covered without mocking the filesystem boundary.
/// </summary>
[TestFixture]
public sealed class JsonVideoRepositoryFullTests
{
    private string _tempRoot = null!;
    private IFileStorageService _storage = null!;
    private JsonVideoRepository _repo = null!;

    [SetUp]
    public void Setup()
    {
        _tempRoot = Directory.CreateTempSubdirectory("video-repo-").FullName;

        _storage = Substitute.For<IFileStorageService>();
        _storage.GetVideosRootPath().Returns(_tempRoot);
        _storage.GetMetadataFilePath(Arg.Any<int>())
            .Returns(call => Path.Combine(_tempRoot, call.Arg<int>().ToString(), "metadata.json"));
        _storage.GetVideoDirectoryPath(Arg.Any<int>())
            .Returns(call => Path.Combine(_tempRoot, call.Arg<int>().ToString()));
        _storage.VideoDirectoryExists(Arg.Any<int>())
            .Returns(call => Directory.Exists(Path.Combine(_tempRoot, call.Arg<int>().ToString())));
        _storage.When(s => s.EnsureVideoDirectoryExists(Arg.Any<int>()))
            .Do(call => Directory.CreateDirectory(
                Path.Combine(_tempRoot, call.Arg<int>().ToString())));
        _storage.DeleteVideoDirectoryAsync(Arg.Any<int>())
            .Returns(call =>
            {
                var dir = Path.Combine(_tempRoot, call.Arg<int>().ToString());
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
                return Task.CompletedTask;
            });

        _repo = new JsonVideoRepository(_storage);
    }

    [TearDown]
    public void TearDown()
    {
        try { if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, true); }
        catch { }
    }

    [Test]
    public async Task SaveAsync_PersistsAndGetByIdAsyncRoundTrips()
    {
        var video = Video.Create(
            1, "Title", "Desc", "v.mp4", "Kraków", VideoCategories.Running, 1234);
        video.Latitude = 50.06;
        video.Longitude = 19.94;

        await _repo.SaveAsync(video);
        var loaded = await _repo.GetByIdAsync(1);

        Assert.That(loaded, Is.Not.Null);
        Assert.That(loaded!.Title, Is.EqualTo("Title"));
        Assert.That(loaded.FileName, Is.EqualTo("v.mp4"));
        Assert.That(loaded.Latitude, Is.EqualTo(50.06));
        Assert.That(loaded.HasCoordinates, Is.True);
    }

    [Test]
    public async Task SaveAsync_WithTraining_RoundTripsTrainingData()
    {
        var video = Video.Create(
            5, "Run", "", "r.mp4", null, VideoCategories.Running, 0);
        video.Training = new TrainingData
        {
            Source = TrainingSource.Strava,
            ExternalId = "999",
            ActivityType = "Run",
            DistanceMeters = 5000,
            AverageHeartRate = 150
        };

        await _repo.SaveAsync(video);
        var loaded = await _repo.GetByIdAsync(5);

        Assert.That(loaded!.Training, Is.Not.Null);
        Assert.That(loaded.Training!.Source, Is.EqualTo(TrainingSource.Strava));
        Assert.That(loaded.Training.ExternalId, Is.EqualTo("999"));
        Assert.That(loaded.Training.DistanceMeters, Is.EqualTo(5000));
    }

    [Test]
    public async Task GetByIdAsync_NoMetadataFile_ReturnsNull()
    {
        var loaded = await _repo.GetByIdAsync(404);

        Assert.That(loaded, Is.Null);
    }

    [Test]
    public async Task GetAllAsync_NoVideosRoot_ReturnsEmpty()
    {
        _storage.GetVideosRootPath().Returns(Path.Combine(_tempRoot, "does-not-exist"));

        var all = await _repo.GetAllAsync();

        Assert.That(all, Is.Empty);
    }

    [Test]
    public async Task GetAllAsync_DirectoryWithoutMetadata_IsSkipped()
    {
        Directory.CreateDirectory(Path.Combine(_tempRoot, "11"));
        // no metadata.json in /11

        await _repo.SaveAsync(Video.Create(
            12, "Has metadata", "", "v.mp4", null, VideoCategories.Running, 0));

        var all = await _repo.GetAllAsync();

        Assert.That(all, Has.Count.EqualTo(1));
        Assert.That(all[0].Id, Is.EqualTo(12));
    }

    [Test]
    public async Task GetAllAsync_ReturnsAllSavedVideos()
    {
        await _repo.SaveAsync(Video.Create(1, "A", "", "a.mp4", null, VideoCategories.Running, 0));
        await _repo.SaveAsync(Video.Create(2, "B", "", "b.mp4", null, VideoCategories.Bouldering, 0));
        await _repo.SaveAsync(Video.Create(3, "C", "", "c.mp4", null, VideoCategories.Gory, 0));

        var all = await _repo.GetAllAsync();

        Assert.That(all, Has.Count.EqualTo(3));
        Assert.That(all.Select(v => v.Id), Is.EquivalentTo(new[] { 1, 2, 3 }));
    }

    [Test]
    public async Task DeleteAsync_RemovesFolderAndReturnsTrue()
    {
        await _repo.SaveAsync(Video.Create(
            7, "Delete me", "", "v.mp4", null, VideoCategories.Running, 0));

        var deleted = await _repo.DeleteAsync(7);

        Assert.That(deleted, Is.True);
        Assert.That(Directory.Exists(Path.Combine(_tempRoot, "7")), Is.False);
    }

    [Test]
    public async Task DeleteAsync_MissingFolder_ReturnsFalse()
    {
        var deleted = await _repo.DeleteAsync(999);

        Assert.That(deleted, Is.False);
    }

    [Test]
    public void GenerateNextId_NoVideosRoot_ReturnsOne()
    {
        _storage.GetVideosRootPath().Returns(Path.Combine(_tempRoot, "missing"));

        Assert.That(_repo.GenerateNextId(), Is.EqualTo(1));
    }

    [Test]
    public void GenerateNextId_EmptyRoot_ReturnsOne()
    {
        Assert.That(_repo.GenerateNextId(), Is.EqualTo(1));
    }

    [Test]
    public async Task GenerateNextId_ExistingVideos_ReturnsMaxPlusOne()
    {
        await _repo.SaveAsync(Video.Create(2, "x", "", "a.mp4", null, VideoCategories.Running, 0));
        await _repo.SaveAsync(Video.Create(5, "y", "", "b.mp4", null, VideoCategories.Running, 0));
        await _repo.SaveAsync(Video.Create(3, "z", "", "c.mp4", null, VideoCategories.Running, 0));

        Assert.That(_repo.GenerateNextId(), Is.EqualTo(6));
    }

    [Test]
    public void GenerateNextId_NonNumericFolders_AreIgnored()
    {
        Directory.CreateDirectory(Path.Combine(_tempRoot, "garbage"));
        Directory.CreateDirectory(Path.Combine(_tempRoot, "13"));

        Assert.That(_repo.GenerateNextId(), Is.EqualTo(14));
    }
}
