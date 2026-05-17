namespace MyHomePage.Tests.Services;

/// <summary>
/// Tests for <see cref="JsonCoachReportRepository"/> — exercises the real
/// filesystem against a temp directory so save/list/get are end-to-end and
/// the ISO-week validation regex is enforced.
/// </summary>
[TestFixture]
public sealed class JsonCoachReportRepositoryTests
{
    private string _tempRoot = null!;
    private IFileStorageService _storage = null!;
    private JsonCoachReportRepository _repo = null!;

    [SetUp]
    public void Setup()
    {
        _tempRoot = Directory.CreateTempSubdirectory("coach-reports-").FullName;
        _storage = Substitute.For<IFileStorageService>();
        _storage.GetVideosRootPath().Returns(_tempRoot);

        var logger = Substitute.For<ILogger<JsonCoachReportRepository>>();
        _repo = new JsonCoachReportRepository(_storage, logger);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    [Test]
    public async Task ListAsync_NoDirectory_ReturnsEmpty()
    {
        var reports = await _repo.ListAsync();

        Assert.That(reports, Is.Empty);
    }

    [Test]
    public async Task SaveAsync_PersistsReportAndGetRoundTrips()
    {
        var original = MakeReport("2026-W20", new DateTime(2026, 5, 11, 0, 0, 0, DateTimeKind.Utc));

        await _repo.SaveAsync(original);
        var fetched = await _repo.GetAsync("2026-W20");

        Assert.That(fetched, Is.Not.Null);
        Assert.That(fetched!.IsoWeek, Is.EqualTo("2026-W20"));
        Assert.That(fetched.Headline, Is.EqualTo("Solid week, controlled load."));
        Assert.That(fetched.Highlights, Has.Count.EqualTo(2));
        Assert.That(fetched.Concerns.First(), Is.EqualTo("HR drift on long run"));
        Assert.That(fetched.KeyMetrics, Has.Count.EqualTo(1));
        Assert.That(fetched.KeyMetrics[0].Value, Is.EqualTo("42 km"));
    }

    [Test]
    public async Task SaveAsync_InvalidIsoWeek_Throws()
    {
        var report = MakeReport("not-a-week", DateTime.UtcNow);

        Assert.ThrowsAsync<ArgumentException>(async () => await _repo.SaveAsync(report));
    }

    [Test]
    public async Task GetAsync_InvalidIsoWeek_ReturnsNullWithoutTouchingDisk()
    {
        var fetched = await _repo.GetAsync("garbage");

        Assert.That(fetched, Is.Null);
    }

    [Test]
    public async Task GetAsync_UnknownButValidWeek_ReturnsNull()
    {
        var fetched = await _repo.GetAsync("2099-W01");

        Assert.That(fetched, Is.Null);
    }

    [Test]
    public async Task ListAsync_OrdersByWeekStartDescending()
    {
        await _repo.SaveAsync(MakeReport("2026-W18", new DateTime(2026, 4, 27, 0, 0, 0, DateTimeKind.Utc)));
        await _repo.SaveAsync(MakeReport("2026-W20", new DateTime(2026, 5, 11, 0, 0, 0, DateTimeKind.Utc)));
        await _repo.SaveAsync(MakeReport("2026-W19", new DateTime(2026, 5, 4, 0, 0, 0, DateTimeKind.Utc)));

        var listed = await _repo.ListAsync();

        Assert.That(listed.Select(r => r.IsoWeek).ToArray(),
            Is.EqualTo(new[] { "2026-W20", "2026-W19", "2026-W18" }));
    }

    [Test]
    public async Task ListAsync_CorruptFile_IsSkippedGracefully()
    {
        var directory = Path.Combine(_tempRoot, "coach-reports");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "2026-W20.json"), "{ not valid json");

        // Add one valid file alongside the corrupt one.
        await _repo.SaveAsync(MakeReport("2026-W21", new DateTime(2026, 5, 18, 0, 0, 0, DateTimeKind.Utc)));

        var listed = await _repo.ListAsync();

        Assert.That(listed, Has.Count.EqualTo(1));
        Assert.That(listed[0].IsoWeek, Is.EqualTo("2026-W21"));
    }

    [Test]
    public async Task SaveAsync_OverwritesPreviousReportForSameWeek()
    {
        await _repo.SaveAsync(MakeReport("2026-W22", DateTime.UtcNow, headline: "Draft headline"));
        await _repo.SaveAsync(MakeReport("2026-W22", DateTime.UtcNow, headline: "Final headline"));

        var current = await _repo.GetAsync("2026-W22");

        Assert.That(current, Is.Not.Null);
        Assert.That(current!.Headline, Is.EqualTo("Final headline"));

        var listed = await _repo.ListAsync();
        Assert.That(listed, Has.Count.EqualTo(1));
    }

    private static CoachReport MakeReport(
        string isoWeek,
        DateTime weekStart,
        string headline = "Solid week, controlled load.") => new()
    {
        IsoWeek = isoWeek,
        WeekStartUtc = weekStart,
        WeekEndUtc = weekStart.AddDays(6),
        GeneratedAtUtc = DateTime.UtcNow,
        Model = "claude-sonnet-test",
        Headline = headline,
        Narrative = "You ran three times and climbed twice. Aerobic base is trending up.",
        Highlights = new[] { "Easy run pace dropped", "First V5 send" },
        Concerns = new[] { "HR drift on long run" },
        NextWeekFocus = new[] { "Cap easy runs at Z2" },
        KeyMetrics = new[]
        {
            new CoachKeyMetric { Label = "Total distance", Value = "42 km" }
        }
    };
}
