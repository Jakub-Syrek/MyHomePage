namespace MyHomePage.Tests.Services;

/// <summary>
/// Unit tests for <see cref="TrainingStatsService"/>. The service is a pure
/// LINQ aggregator so the fixtures here build in-memory <see cref="Video"/>
/// graphs and assert on the resulting <see cref="TrainingStats"/> shape —
/// no IO, no mocks beyond the data.
/// </summary>
[TestFixture]
public sealed class TrainingStatsServiceTests
{
    private TrainingStatsService _service = null!;

    [SetUp]
    public void Setup()
    {
        _service = new TrainingStatsService();
    }

    [Test]
    public void Compute_EmptyList_ReturnsZeroSessionsAndBoundedWindow()
    {
        var from = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 1, 31, 23, 59, 59, DateTimeKind.Utc);

        var stats = _service.Compute(Array.Empty<Video>(), from, to);

        Assert.Multiple(() =>
        {
            Assert.That(stats.SessionCount, Is.EqualTo(0));
            Assert.That(stats.TotalDistanceMeters, Is.EqualTo(0));
            Assert.That(stats.TotalDuration, Is.EqualTo(TimeSpan.Zero));
            Assert.That(stats.WeeklyBuckets, Is.Empty);
            Assert.That(stats.RecentRecords, Is.Empty);
            Assert.That(stats.FromUtc.Date, Is.EqualTo(from.Date));
            Assert.That(stats.ToUtc.Date, Is.EqualTo(to.Date));
        });
    }

    [Test]
    public void Compute_VideosWithoutTraining_AreIgnored()
    {
        var media = MakeVideo(1, "Spring photo dump", VideoCategories.Gory, training: null);
        var run = MakeRunVideo(2, "Morning Run", new DateTime(2026, 5, 1, 6, 0, 0, DateTimeKind.Utc),
            distanceMeters: 5000, durationMinutes: 30);

        var stats = _service.Compute(
            new[] { media, run },
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 12, 31, 23, 59, 59, DateTimeKind.Utc));

        Assert.That(stats.SessionCount, Is.EqualTo(1));
        Assert.That(stats.TotalDistanceMeters, Is.EqualTo(5000));
    }

    [Test]
    public void Compute_OutsideWindow_AreIgnored()
    {
        var before = MakeRunVideo(1, "Old Run",
            new DateTime(2025, 12, 1, 6, 0, 0, DateTimeKind.Utc),
            distanceMeters: 8000, durationMinutes: 50);
        var inside = MakeRunVideo(2, "Recent Run",
            new DateTime(2026, 5, 1, 6, 0, 0, DateTimeKind.Utc),
            distanceMeters: 5000, durationMinutes: 30);
        var after = MakeRunVideo(3, "Future Run",
            new DateTime(2027, 1, 1, 6, 0, 0, DateTimeKind.Utc),
            distanceMeters: 10000, durationMinutes: 60);

        var stats = _service.Compute(
            new[] { before, inside, after },
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 12, 31, 23, 59, 59, DateTimeKind.Utc));

        Assert.That(stats.SessionCount, Is.EqualTo(1));
        Assert.That(stats.TotalDistanceMeters, Is.EqualTo(5000));
    }

    [Test]
    public void Compute_AggregatesPerCategorySessionAndDistance()
    {
        var run = MakeRunVideo(1, "Run A",
            new DateTime(2026, 5, 1, 6, 0, 0, DateTimeKind.Utc),
            distanceMeters: 6000, durationMinutes: 40);
        var hike = MakeVideo(2, "Hike",
            VideoCategories.Gory,
            training: MakeTraining(
                new DateTime(2026, 5, 2, 7, 0, 0, DateTimeKind.Utc),
                distanceMeters: 12000,
                durationMinutes: 180));
        var run2 = MakeRunVideo(3, "Run B",
            new DateTime(2026, 5, 3, 6, 0, 0, DateTimeKind.Utc),
            distanceMeters: 5500, durationMinutes: 35);

        var stats = _service.Compute(
            new[] { run, hike, run2 },
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 12, 31, 23, 59, 59, DateTimeKind.Utc));

        Assert.Multiple(() =>
        {
            Assert.That(stats.SessionsByCategory[VideoCategories.Running], Is.EqualTo(2));
            Assert.That(stats.SessionsByCategory[VideoCategories.Gory], Is.EqualTo(1));
            Assert.That(stats.DistanceByCategory[VideoCategories.Running], Is.EqualTo(11500));
            Assert.That(stats.DistanceByCategory[VideoCategories.Gory], Is.EqualTo(12000));
        });
    }

    [Test]
    public void Compute_WeeklyBuckets_FillsGapsWithZeroRows()
    {
        // Two sessions: first on 2026-05-04 (Mon of W19), second on 2026-05-25
        // (Mon of W22). The two-week gap (W20, W21) must appear as zero-rows.
        var sessions = new[]
        {
            MakeRunVideo(1, "Week 19", new DateTime(2026, 5, 4, 6, 0, 0, DateTimeKind.Utc),
                distanceMeters: 5000, durationMinutes: 30),
            MakeRunVideo(2, "Week 22", new DateTime(2026, 5, 25, 6, 0, 0, DateTimeKind.Utc),
                distanceMeters: 8000, durationMinutes: 50)
        };

        var stats = _service.Compute(
            sessions,
            new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 5, 31, 23, 59, 59, DateTimeKind.Utc));

        Assert.That(stats.WeeklyBuckets, Has.Count.EqualTo(4));
        Assert.Multiple(() =>
        {
            Assert.That(stats.WeeklyBuckets[0].SessionCount, Is.EqualTo(1));
            Assert.That(stats.WeeklyBuckets[1].SessionCount, Is.EqualTo(0));
            Assert.That(stats.WeeklyBuckets[2].SessionCount, Is.EqualTo(0));
            Assert.That(stats.WeeklyBuckets[3].SessionCount, Is.EqualTo(1));
            Assert.That(stats.WeeklyBuckets[0].DistanceMeters, Is.EqualTo(5000));
            Assert.That(stats.WeeklyBuckets[3].DistanceMeters, Is.EqualTo(8000));
        });
    }

    [Test]
    public void Compute_WeeklyBucket_AnchorsOnMonday()
    {
        // 2026-05-07 is a Thursday. Its week starts on Monday 2026-05-04.
        var session = MakeRunVideo(1, "Thursday Run",
            new DateTime(2026, 5, 7, 6, 0, 0, DateTimeKind.Utc),
            distanceMeters: 5000, durationMinutes: 30);

        var stats = _service.Compute(
            new[] { session },
            new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 5, 31, 23, 59, 59, DateTimeKind.Utc));

        Assert.That(stats.WeeklyBuckets, Has.Count.EqualTo(1));
        Assert.That(stats.WeeklyBuckets[0].WeekStartUtc.DayOfWeek, Is.EqualTo(DayOfWeek.Monday));
        Assert.That(stats.WeeklyBuckets[0].WeekStartUtc.Date,
            Is.EqualTo(new DateTime(2026, 5, 4)));
        Assert.That(stats.WeeklyBuckets[0].Label, Does.Match("^\\d{4}-W\\d{2}$"));
    }

    [Test]
    public void Compute_LongestStreak_CountsConsecutiveDistinctDays()
    {
        var basis = new DateTime(2026, 5, 1, 6, 0, 0, DateTimeKind.Utc);
        var sessions = new[]
        {
            MakeRunVideo(1, "Day 1", basis, 5000, 30),
            MakeRunVideo(2, "Day 2", basis.AddDays(1), 5000, 30),
            MakeRunVideo(3, "Day 3", basis.AddDays(2), 5000, 30),
            MakeRunVideo(4, "Day 3 second", basis.AddDays(2).AddHours(6), 4000, 25), // same day
            MakeRunVideo(5, "Day 5 (after rest)", basis.AddDays(4), 5000, 30),
            MakeRunVideo(6, "Day 6", basis.AddDays(5), 5000, 30)
        };

        var stats = _service.Compute(
            sessions,
            basis.AddDays(-30),
            basis.AddDays(30));

        Assert.That(stats.LongestStreakDays, Is.EqualTo(3));
    }

    [Test]
    public void Compute_CurrentStreak_AnchoredOnToday()
    {
        var today = DateTime.UtcNow.Date;
        var sessions = new[]
        {
            MakeRunVideo(1, "Today",     today.AddHours(7),  5000, 30),
            MakeRunVideo(2, "Yesterday", today.AddDays(-1).AddHours(7), 4000, 25),
            MakeRunVideo(3, "2 days ago",today.AddDays(-2).AddHours(7), 3000, 20),
            MakeRunVideo(4, "4 days ago",today.AddDays(-4).AddHours(7), 6000, 35) // break in chain
        };

        var stats = _service.Compute(
            sessions,
            today.AddDays(-30),
            today.AddDays(1));

        Assert.That(stats.CurrentStreakDays, Is.EqualTo(3));
    }

    [Test]
    public void Compute_RecentRecords_OnlyIncludesRank1Efforts_NewestFirst()
    {
        var first = MakeRunVideo(1, "May run",
            new DateTime(2026, 5, 1, 6, 0, 0, DateTimeKind.Utc),
            distanceMeters: 10000, durationMinutes: 60);
        first.Training = first.Training! with
        {
            BestEfforts = new[]
            {
                new TrainingBestEffort
                {
                    Name = "5k",
                    DistanceMeters = 5000,
                    Duration = TimeSpan.FromMinutes(25),
                    PersonalRecordRank = 1
                },
                new TrainingBestEffort
                {
                    Name = "10k",
                    DistanceMeters = 10000,
                    Duration = TimeSpan.FromMinutes(55),
                    PersonalRecordRank = 2  // not a PR
                }
            }
        };

        var second = MakeRunVideo(2, "June run",
            new DateTime(2026, 6, 1, 6, 0, 0, DateTimeKind.Utc),
            distanceMeters: 10000, durationMinutes: 58);
        second.Training = second.Training! with
        {
            BestEfforts = new[]
            {
                new TrainingBestEffort
                {
                    Name = "5k",
                    DistanceMeters = 5000,
                    Duration = TimeSpan.FromMinutes(24),
                    PersonalRecordRank = 1
                }
            }
        };

        var stats = _service.Compute(
            new[] { first, second },
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 12, 31, 23, 59, 59, DateTimeKind.Utc));

        Assert.That(stats.RecentRecords, Has.Count.EqualTo(2));
        Assert.That(stats.RecentRecords[0].VideoId, Is.EqualTo(2),
            "Most recent PR should be first.");
        Assert.That(stats.RecentRecords[0].Name, Is.EqualTo("5k"));
        Assert.That(stats.RecentRecords.All(r => r.SessionTitle.Length > 0));
    }

    [Test]
    public void Compute_AggregatesSufferAndPrCounts()
    {
        var run = MakeRunVideo(1, "Hard run",
            new DateTime(2026, 5, 1, 6, 0, 0, DateTimeKind.Utc),
            distanceMeters: 10000, durationMinutes: 55);
        run.Training = run.Training! with
        {
            SufferScore = 180,
            PersonalRecordCount = 2,
            AchievementCount = 3,
            Calories = 720,
            ElevationGainMeters = 50
        };
        var easy = MakeRunVideo(2, "Easy run",
            new DateTime(2026, 5, 2, 6, 0, 0, DateTimeKind.Utc),
            distanceMeters: 6000, durationMinutes: 40);
        easy.Training = easy.Training! with
        {
            SufferScore = 50,
            PersonalRecordCount = 0,
            AchievementCount = 1,
            Calories = 380,
            ElevationGainMeters = 12
        };

        var stats = _service.Compute(
            new[] { run, easy },
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 12, 31, 23, 59, 59, DateTimeKind.Utc));

        Assert.Multiple(() =>
        {
            Assert.That(stats.TotalSufferScore, Is.EqualTo(230));
            Assert.That(stats.TotalPersonalRecords, Is.EqualTo(2));
            Assert.That(stats.TotalAchievements, Is.EqualTo(4));
            Assert.That(stats.TotalCalories, Is.EqualTo(1100));
            Assert.That(stats.TotalElevationGainMeters, Is.EqualTo(62));
        });
    }

    // ── Fixtures ────────────────────────────────────────────────────────

    private static Video MakeVideo(
        int id, string title, string category, TrainingData? training)
    {
        var video = Video.Create(
            id, title, "desc", "video.mp4",
            location: null, category: category, fileSizeBytes: 0);
        video.Training = training;
        return video;
    }

    private static Video MakeRunVideo(
        int id, string title, DateTime startUtc,
        double distanceMeters, int durationMinutes)
    {
        var video = Video.Create(
            id, title, "desc", "video.mp4",
            location: null, category: VideoCategories.Running, fileSizeBytes: 0);
        video.Training = MakeTraining(startUtc, distanceMeters, durationMinutes);
        return video;
    }

    private static TrainingData MakeTraining(
        DateTime startUtc, double distanceMeters, int durationMinutes) => new()
    {
        Source = TrainingSource.Strava,
        ExternalId = Guid.NewGuid().ToString("N").Substring(0, 8),
        ActivityType = "Run",
        StartTimeUtc = startUtc,
        Duration = TimeSpan.FromMinutes(durationMinutes),
        DistanceMeters = distanceMeters
    };
}
