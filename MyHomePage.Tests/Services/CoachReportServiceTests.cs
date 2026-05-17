namespace MyHomePage.Tests.Services;

/// <summary>
/// Tests for <see cref="CoachReportService"/>. The orchestrator is exercised
/// with mocked collaborators so the assertions focus on its own logic:
/// AI-disabled short-circuit, empty-week rejection, persistence wiring, ISO
/// week id formatting and key-metric construction.
/// </summary>
[TestFixture]
public sealed class CoachReportServiceTests
{
    private IVideoService _videos = null!;
    private IAiAssistantService _ai = null!;
    private ICoachReportRepository _reports = null!;
    private TrainingStatsService _stats = null!;
    private CoachReportService _service = null!;

    [SetUp]
    public void Setup()
    {
        _videos = Substitute.For<IVideoService>();
        _ai = Substitute.For<IAiAssistantService>();
        _reports = Substitute.For<ICoachReportRepository>();
        _stats = new TrainingStatsService();

        var logger = Substitute.For<ILogger<CoachReportService>>();
        _service = new CoachReportService(_videos, _stats, _ai, _reports, logger);
    }

    [Test]
    public async Task GenerateForWeekAsync_AssistantDisabled_ReturnsFailureWithoutSaving()
    {
        _ai.IsEnabled.Returns(false);

        var result = await _service.GenerateForWeekAsync(new DateTime(2026, 5, 14));

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("ANTHROPIC_API_KEY"));
        await _reports.DidNotReceive().SaveAsync(Arg.Any<CoachReport>(), Arg.Any<CancellationToken>());
        await _ai.DidNotReceive().GenerateCoachReportAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GenerateForWeekAsync_NoSessionsInWeek_ReturnsFailure()
    {
        _ai.IsEnabled.Returns(true);
        _videos.GetAllVideosAsync().Returns(Array.Empty<Video>());

        var result = await _service.GenerateForWeekAsync(new DateTime(2026, 5, 14));

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("No training sessions"));
        await _ai.DidNotReceive().GenerateCoachReportAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GenerateForWeekAsync_AiReturnsNull_ReturnsFailure()
    {
        _ai.IsEnabled.Returns(true);
        _videos.GetAllVideosAsync().Returns(new[]
        {
            VideoWithTraining(1, new DateTime(2026, 5, 14, 6, 0, 0, DateTimeKind.Utc))
        });
        _ai.GenerateCoachReportAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((CoachReportPayload?)null);

        var result = await _service.GenerateForWeekAsync(new DateTime(2026, 5, 14));

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("could not generate"));
        await _reports.DidNotReceive().SaveAsync(Arg.Any<CoachReport>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GenerateForWeekAsync_Success_PersistsReportWithExpectedShape()
    {
        _ai.IsEnabled.Returns(true);
        _videos.GetAllVideosAsync().Returns(new[]
        {
            // Two sessions in week starting Mon 2026-05-11 (W20).
            VideoWithTraining(1, new DateTime(2026, 5, 14, 6, 0, 0, DateTimeKind.Utc), 5000),
            VideoWithTraining(2, new DateTime(2026, 5, 15, 17, 0, 0, DateTimeKind.Utc), 10000)
        });
        _ai.GenerateCoachReportAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CoachReportPayload
            {
                Headline = "Great week",
                Narrative = "Two runs, solid build.",
                Highlights = new[] { "5k easy" },
                Concerns = new[] { "watch HR" },
                NextWeekFocus = new[] { "long run" }
            });

        var result = await _service.GenerateForWeekAsync(new DateTime(2026, 5, 14));

        Assert.That(result.IsSuccess, Is.True);
        var report = result.Value!;
        Assert.That(report.IsoWeek, Is.EqualTo("2026-W20"));
        Assert.That(report.WeekStartUtc.DayOfWeek, Is.EqualTo(DayOfWeek.Monday));
        Assert.That(report.WeekStartUtc, Is.EqualTo(new DateTime(2026, 5, 11, 0, 0, 0, DateTimeKind.Utc)));
        Assert.That(report.WeekEndUtc.Date, Is.EqualTo(new DateTime(2026, 5, 17)));
        Assert.That(report.Headline, Is.EqualTo("Great week"));
        Assert.That(report.Narrative, Is.EqualTo("Two runs, solid build."));
        Assert.That(report.Highlights, Is.EqualTo(new[] { "5k easy" }));
        Assert.That(report.Concerns, Is.EqualTo(new[] { "watch HR" }));
        Assert.That(report.NextWeekFocus, Is.EqualTo(new[] { "long run" }));

        // Key metrics must always include sessions / distance / time / elevation.
        var labels = report.KeyMetrics.Select(m => m.Label).ToArray();
        Assert.That(labels, Does.Contain("Sessions"));
        Assert.That(labels, Does.Contain("Distance"));
        Assert.That(labels, Does.Contain("Moving time"));
        Assert.That(labels, Does.Contain("Elevation"));
        var sessions = report.KeyMetrics.First(m => m.Label == "Sessions").Value;
        Assert.That(sessions, Is.EqualTo("2"));

        await _reports.Received(1).SaveAsync(
            Arg.Is<CoachReport>(r => r.IsoWeek == "2026-W20"),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GenerateForWeekAsync_AnchorsOnMondayRegardlessOfInputDay()
    {
        _ai.IsEnabled.Returns(true);
        _videos.GetAllVideosAsync().Returns(new[]
        {
            VideoWithTraining(1, new DateTime(2026, 5, 11, 6, 0, 0, DateTimeKind.Utc))
        });
        _ai.GenerateCoachReportAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CoachReportPayload { Headline = "h", Narrative = "n" });

        // Sunday 2026-05-17 → still belongs to week starting Mon 2026-05-11.
        var result = await _service.GenerateForWeekAsync(new DateTime(2026, 5, 17));

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.WeekStartUtc, Is.EqualTo(new DateTime(2026, 5, 11, 0, 0, 0, DateTimeKind.Utc)));
    }

    [Test]
    public async Task ListAsync_DelegatesToRepository()
    {
        var reports = new[] { new CoachReport { IsoWeek = "2026-W20" } };
        _reports.ListAsync(Arg.Any<CancellationToken>()).Returns(reports);

        var result = await _service.ListAsync();

        Assert.That(result, Is.SameAs(reports));
        await _reports.Received(1).ListAsync(Arg.Any<CancellationToken>());
    }

    private static Video VideoWithTraining(int id, DateTime startUtc, double distanceMeters = 5000)
    {
        var video = Video.Create(
            id: id, title: $"Session {id}", description: "", fileName: $"v{id}.mp4",
            location: null, category: VideoCategories.Running, fileSizeBytes: 0);
        video.Training = new TrainingData
        {
            Source = TrainingSource.Strava,
            ExternalId = id.ToString(),
            ActivityType = "Run",
            StartTimeUtc = startUtc,
            Duration = TimeSpan.FromMinutes(30),
            DistanceMeters = distanceMeters,
            ElevationGainMeters = 25
        };
        return video;
    }
}
