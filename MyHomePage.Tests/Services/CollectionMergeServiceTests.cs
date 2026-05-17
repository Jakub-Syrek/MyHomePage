namespace MyHomePage.Tests.Services;

/// <summary>
/// End-to-end tests for <see cref="CollectionMergeService"/> backed by a
/// temp directory and a stub <see cref="IVideoRepository"/>. Verifies
/// the aggregator math (totals + weighted HR average), file copy with
/// per-source prefix, source deletion, and the generated summary.md.
/// </summary>
[TestFixture]
public sealed class CollectionMergeServiceTests
{
    private string _tempRoot = null!;
    private IFileStorageService _storage = null!;
    private InMemoryVideoRepository _repo = null!;
    private CollectionMergeService _service = null!;

    [SetUp]
    public void Setup()
    {
        _tempRoot = Directory.CreateTempSubdirectory("merge-").FullName;
        _storage = Substitute.For<IFileStorageService>();
        _storage.GetVideosRootPath().Returns(_tempRoot);
        _storage.GetVideoDirectoryPath(Arg.Any<int>())
            .Returns(call => Path.Combine(_tempRoot, ((int)call[0]).ToString()));
        _storage.When(s => s.EnsureVideoDirectoryExists(Arg.Any<int>()))
            .Do(call => Directory.CreateDirectory(Path.Combine(_tempRoot, ((int)call[0]).ToString())));
        _storage.DeleteVideoDirectoryAsync(Arg.Any<int>())
            .Returns(call =>
            {
                var dir = Path.Combine(_tempRoot, ((int)call[0]).ToString());
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
                return Task.CompletedTask;
            });

        _repo = new InMemoryVideoRepository();
        var logger = Substitute.For<ILogger<CollectionMergeService>>();
        _service = new CollectionMergeService(_repo, _storage, logger);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true);
    }

    [Test]
    public async Task Merge_RequiresAtLeastTwoSources()
    {
        var result = await _service.MergeAsync(new[] { 1 }, "X", "y");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("at least two"));
    }

    [Test]
    public async Task Merge_RequiresTitle()
    {
        SeedVideo(1, training: BuildTraining("Run", 30, calories: 200, hr: 150));
        SeedVideo(2, training: BuildTraining("WeightTraining", 45, calories: 250, hr: 130));

        var result = await _service.MergeAsync(new[] { 1, 2 }, "  ", string.Empty);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("Title"));
    }

    [Test]
    public async Task Merge_SumsDurationCaloriesAndEffort()
    {
        SeedVideo(1, training: BuildTraining("Run", 30, calories: 200, hr: 150, effort: 60));
        SeedVideo(2, training: BuildTraining("WeightTraining", 45, calories: 250, hr: 130, effort: 40));

        var result = await _service.MergeAsync(new[] { 1, 2 }, "Multi", "desc");

        Assert.That(result.IsSuccess, Is.True);
        var merged = await _repo.GetByIdAsync(result.Value);
        Assert.That(merged, Is.Not.Null);
        Assert.That(merged!.Training, Is.Not.Null);
        Assert.That(merged.Training!.IsMultiSport, Is.True);
        Assert.That(merged.Training.Duration.TotalMinutes, Is.EqualTo(75));
        Assert.That(merged.Training.Calories, Is.EqualTo(450));
        Assert.That(merged.Training.SufferScore, Is.EqualTo(100));
        Assert.That(merged.Training.SubActivities!.Count, Is.EqualTo(2));
    }

    [Test]
    public async Task Merge_WeightsAverageHeartRateByDuration()
    {
        // 30 min @ 150 bpm + 60 min @ 120 bpm  →  (150*30 + 120*60) / 90  ≈ 130
        SeedVideo(1, training: BuildTraining("Run", 30, hr: 150));
        SeedVideo(2, training: BuildTraining("Ride", 60, hr: 120));

        var result = await _service.MergeAsync(new[] { 1, 2 }, "Mix", string.Empty);

        Assert.That(result.IsSuccess, Is.True);
        var merged = (await _repo.GetByIdAsync(result.Value))!;
        Assert.That(merged.Training!.AverageHeartRate, Is.EqualTo(130));
    }

    [Test]
    public async Task Merge_RetainsSourceCollectionsAndDirectories()
    {
        // Sources are now snapshots of an aggregate view, not consumed by it.
        SeedVideoWithFile(1, "original.mp4");
        SeedVideoWithFile(2, "original.mp4");

        var result = await _service.MergeAsync(new[] { 1, 2 }, "Combined", string.Empty);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(await _repo.GetByIdAsync(1), Is.Not.Null);
        Assert.That(await _repo.GetByIdAsync(2), Is.Not.Null);
        Assert.That(Directory.Exists(Path.Combine(_tempRoot, "1")), Is.True);
        Assert.That(Directory.Exists(Path.Combine(_tempRoot, "2")), Is.True);
    }

    [Test]
    public async Task Merge_CopiesUserMediaWithSourceIdPrefix()
    {
        SeedVideoWithFile(1, "original.mp4");
        SeedVideoWithFile(2, "original.mp4");

        var result = await _service.MergeAsync(new[] { 1, 2 }, "Combined", string.Empty);
        Assert.That(result.IsSuccess, Is.True);

        var newDir = Path.Combine(_tempRoot, result.Value.ToString());
        var copied = Directory.GetFiles(newDir).Select(Path.GetFileName).ToHashSet();
        Assert.That(copied, Does.Contain("s1-original.mp4"));
        Assert.That(copied, Does.Contain("s2-original.mp4"));
    }

    [Test]
    public async Task Merge_AlwaysLandsInMultiSportCategory()
    {
        SeedVideoWithFile(1, "user.jpg", category: VideoCategories.Calisthenics);
        SeedVideoWithFile(2, "user.jpg", category: VideoCategories.Calisthenics);

        var result = await _service.MergeAsync(new[] { 1, 2 }, "Hybrid", string.Empty);

        Assert.That(result.IsSuccess, Is.True);
        var merged = (await _repo.GetByIdAsync(result.Value))!;
        Assert.That(merged.Category, Is.EqualTo(VideoCategories.MultiSport));
    }

    [Test]
    public async Task Merge_SkipsStumpCoverWhenUserMediaIsPresent()
    {
        // Source 1 is a Strava stump (cover.jpg only), source 2 has a real
        // user photo. The merge keeps the user photo and skips the cover.
        SeedVideoWithFile(1, CollectionMergeService.StumpCoverFileName);
        SeedVideoWithFile(2, "summit.jpg");

        var result = await _service.MergeAsync(new[] { 1, 2 }, "Mixed", string.Empty);
        Assert.That(result.IsSuccess, Is.True);

        var newDir = Path.Combine(_tempRoot, result.Value.ToString());
        var copied = Directory.GetFiles(newDir).Select(Path.GetFileName).ToHashSet();
        Assert.That(copied, Does.Contain("s2-summit.jpg"));
        Assert.That(copied, Does.Not.Contain("s1-cover.jpg"));
    }

    [Test]
    public async Task Merge_AlwaysWritesMosaicCoverAsPrimary()
    {
        // Sources have images (covers and/or user uploads). The merge must
        // produce a single composite "multisport-cover.jpg" at Order = 0.
        // Test bytes are not valid JPEGs, so the in-process ImageSharp
        // compose path throws — the service is expected to catch and fall
        // back to copying the first selected source's thumbnail into the
        // mosaic slot, which keeps the primary name stable.
        SeedVideoWithFile(1, CollectionMergeService.StumpCoverFileName);
        SeedVideoWithFile(2, CollectionMergeService.StumpCoverFileName);

        var result = await _service.MergeAsync(new[] { 2, 1 }, "Stumps only", string.Empty);
        Assert.That(result.IsSuccess, Is.True);

        var newDir = Path.Combine(_tempRoot, result.Value.ToString());
        var copied = Directory.GetFiles(newDir).Select(Path.GetFileName).ToHashSet();
        Assert.That(copied, Does.Contain(CollectionMergeService.MosaicCoverFileName));
        Assert.That(copied, Does.Not.Contain("s1-cover.jpg"));
        Assert.That(copied, Does.Not.Contain("s2-cover.jpg"));

        var merged = (await _repo.GetByIdAsync(result.Value))!;
        Assert.That(merged.FileName, Is.EqualTo(CollectionMergeService.MosaicCoverFileName));
        Assert.That(merged.Media[0].Order, Is.EqualTo(0));
        Assert.That(merged.Media[0].FileName, Is.EqualTo(CollectionMergeService.MosaicCoverFileName));
    }

    [Test]
    public async Task Merge_MosaicCoverSitsAheadOfUserMedia()
    {
        // User photo on source 2 must still be copied, but the mosaic
        // cover takes the primary slot so /multisport cards show the
        // combined view first.
        SeedVideoWithFile(1, CollectionMergeService.StumpCoverFileName);
        SeedVideoWithFile(2, "summit.jpg");

        var result = await _service.MergeAsync(new[] { 1, 2 }, "Hybrid", string.Empty);
        Assert.That(result.IsSuccess, Is.True);

        var merged = (await _repo.GetByIdAsync(result.Value))!;
        Assert.That(merged.Media[0].FileName, Is.EqualTo(CollectionMergeService.MosaicCoverFileName));
        Assert.That(
            merged.Media.Select(m => m.FileName),
            Does.Contain("s2-summit.jpg"));
        Assert.That(merged.FileName, Is.EqualTo(CollectionMergeService.MosaicCoverFileName));
    }

    [Test]
    public async Task Merge_NoImagesAtAll_LeavesMasterWithoutCover()
    {
        // Pure video sources, no covers, no images → no candidate for the
        // mosaic. The master still saves but its FileName ends up empty.
        SeedVideoWithFile(1, "clip.mp4");
        SeedVideoWithFile(2, "clip.mp4");

        var result = await _service.MergeAsync(new[] { 1, 2 }, "Just videos", string.Empty);
        Assert.That(result.IsSuccess, Is.True);

        var merged = (await _repo.GetByIdAsync(result.Value))!;
        Assert.That(merged.Media.Any(m => m.FileName == CollectionMergeService.MosaicCoverFileName), Is.False);
        Assert.That(
            merged.Media.Select(m => m.FileName),
            Is.EquivalentTo(new[] { "s1-clip.mp4", "s2-clip.mp4" }));
    }

    [Test]
    public async Task Merge_WritesSummaryMarkdownWithStravaLinks()
    {
        SeedVideo(1, training: BuildTraining(
            "Run", 30, calories: 200, hr: 150, effort: 60,
            externalId: "9001", externalUrl: "https://www.strava.com/activities/9001"));
        SeedVideo(2, training: BuildTraining(
            "WeightTraining", 45, calories: 250, hr: 130, effort: 40,
            externalId: "9002", externalUrl: "https://www.strava.com/activities/9002"));

        var result = await _service.MergeAsync(new[] { 1, 2 }, "Hybrid day", "double session");
        Assert.That(result.IsSuccess, Is.True);

        var summary = await File.ReadAllTextAsync(
            Path.Combine(_tempRoot, result.Value.ToString(), CollectionMergeService.SummaryFileName));
        Assert.That(summary, Does.Contain("# Multi-sport — Hybrid day"));
        Assert.That(summary, Does.Contain("**Duration:** 1h 15m"));
        Assert.That(summary, Does.Contain("**Calories:** 450 kcal"));
        Assert.That(summary, Does.Contain("[#9001](https://www.strava.com/activities/9001)"));
        Assert.That(summary, Does.Contain("[#9002](https://www.strava.com/activities/9002)"));
    }

    [Test]
    public async Task Merge_NoTrainingOnAnySource_StillProducesCollectionWithoutTotals()
    {
        SeedVideo(1, training: null);
        SeedVideo(2, training: null);

        var result = await _service.MergeAsync(new[] { 1, 2 }, "Photos", string.Empty);
        Assert.That(result.IsSuccess, Is.True);

        var merged = (await _repo.GetByIdAsync(result.Value))!;
        Assert.That(merged.Training, Is.Null);

        var summary = await File.ReadAllTextAsync(
            Path.Combine(_tempRoot, result.Value.ToString(), CollectionMergeService.SummaryFileName));
        Assert.That(summary, Does.Contain("No training data"));
    }

    // ── helpers ────────────────────────────────────────────────────────

    private void SeedVideo(int id, TrainingData? training)
    {
        var v = new Video
        {
            Id = id,
            Title = $"Source {id}",
            Description = "src",
            FileName = string.Empty,
            Category = "Calisthenics",
            UploadedAt = new DateTime(2026, 5, 17, 10 + id, 0, 0, DateTimeKind.Utc),
            Training = training,
        };
        _repo.Seed(v);
    }

    private void SeedVideoWithFile(int id, string fileName, string category = "Calisthenics")
    {
        Directory.CreateDirectory(Path.Combine(_tempRoot, id.ToString()));
        File.WriteAllBytes(Path.Combine(_tempRoot, id.ToString(), fileName), new byte[128]);

        var v = new Video
        {
            Id = id,
            Title = $"Source {id}",
            Description = "src",
            FileName = fileName,
            Category = category,
            UploadedAt = new DateTime(2026, 5, 17, 10 + id, 0, 0, DateTimeKind.Utc),
            Media = { MediaItem.Create(fileName, MediaItem.DetectType(fileName), 128, 0) },
        };
        _repo.Seed(v);
    }

    private static TrainingData BuildTraining(
        string type, int minutes,
        int? calories = null, int? hr = null, int? effort = null,
        string? externalId = null, string? externalUrl = null) => new()
    {
        Source = TrainingSource.Strava,
        ExternalId = externalId ?? string.Empty,
        ExternalUrl = externalUrl,
        ActivityType = type,
        StartTimeUtc = new DateTime(2026, 5, 17, 10, 0, 0, DateTimeKind.Utc),
        Duration = TimeSpan.FromMinutes(minutes),
        Calories = calories,
        AverageHeartRate = hr,
        SufferScore = effort,
    };

    /// <summary>
    /// In-memory <see cref="IVideoRepository"/> stub. Generates monotonically
    /// increasing ids starting from 100 so seeded source ids (1, 2, …) don't
    /// collide with the merged new id.
    /// </summary>
    private sealed class InMemoryVideoRepository : IVideoRepository
    {
        private readonly Dictionary<int, Video> _store = new();
        private int _nextId = 100;

        public void Seed(Video v) => _store[v.Id] = v;

        public Task<IReadOnlyList<Video>> GetAllAsync() =>
            Task.FromResult<IReadOnlyList<Video>>(_store.Values.ToList());

        public Task<Video?> GetByIdAsync(int id) =>
            Task.FromResult(_store.GetValueOrDefault(id));

        public Task SaveAsync(Video video)
        {
            _store[video.Id] = video;
            return Task.CompletedTask;
        }

        public Task<bool> DeleteAsync(int id) =>
            Task.FromResult(_store.Remove(id));

        public int GenerateNextId() => _nextId++;
    }
}
