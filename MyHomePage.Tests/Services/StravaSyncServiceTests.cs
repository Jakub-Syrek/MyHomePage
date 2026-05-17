namespace MyHomePage.Tests.Services;

/// <summary>
/// Unit tests for <see cref="StravaSyncService"/>. The collaborators are
/// substituted with NSubstitute mocks so the tests exercise the orchestration
/// logic only — privacy filtering, fetch failure handling, deduplication.
/// </summary>
[TestFixture]
public sealed class StravaSyncServiceTests
{
    private IStravaApiClient _api = null!;
    private IStravaTokenStore _store = null!;
    private IVideoRepository _videos = null!;
    private IAiAssistantService _ai = null!;
    private IReverseGeocoder _geocoder = null!;
    private IFileStorageService _storage = null!;
    private StravaTokenService _tokens = null!;
    private StravaSyncService _sync = null!;
    private StravaOptions _options = null!;

    [SetUp]
    public void Setup()
    {
        _api = Substitute.For<IStravaApiClient>();
        _store = Substitute.For<IStravaTokenStore>();
        _videos = Substitute.For<IVideoRepository>();
        _ai = Substitute.For<IAiAssistantService>();
        _ai.IsEnabled.Returns(false);
        _geocoder = Substitute.For<IReverseGeocoder>();
        _geocoder.ResolveAsync(
                Arg.Any<double>(), Arg.Any<double>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);
        _storage = Substitute.For<IFileStorageService>();
        _storage.CopyWwwRootFileToVideoAsync(
                Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>())
            .Returns(0L);
        _options = new StravaOptions
        {
            ClientId = "1",
            ClientSecret = "x",
            ImportPublicOnly = true
        };

        var tokenLogger = Substitute.For<ILogger<StravaTokenService>>();
        _tokens = new StravaTokenService(_api, _store, tokenLogger);

        var syncLogger = Substitute.For<ILogger<StravaSyncService>>();
        _sync = new StravaSyncService(
            _api, _tokens, _videos, _ai, _geocoder, _storage,
            Microsoft.Extensions.Options.Options.Create(_options),
            syncLogger);
    }

    [Test]
    public async Task ImportActivityAsync_NoTokensPersisted_ReturnsFailure()
    {
        _store.LoadAsync(Arg.Any<CancellationToken>()).Returns((StravaTokenSet?)null);

        var result = await _sync.ImportActivityAsync(1);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("not been connected"));
    }

    [Test]
    public async Task ImportActivityAsync_FetchFails_PropagatesMessage()
    {
        ArrangeValidTokens();
        _api.GetActivityAsync(Arg.Any<string>(), 42, Arg.Any<CancellationToken>())
            .Returns(OperationResult<StravaActivity>.Failure("Strava API returned 404 Not Found."));

        var result = await _sync.ImportActivityAsync(42);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("404"));
    }

    [Test]
    public async Task ImportActivityAsync_PrivateActivityWithFilterOn_IsRejected()
    {
        ArrangeValidTokens();
        _api.GetActivityAsync(Arg.Any<string>(), 7, Arg.Any<CancellationToken>())
            .Returns(OperationResult<StravaActivity>.Success(
                new StravaActivity { Id = 7, Visibility = "only_me", Type = "Run" }));

        var result = await _sync.ImportActivityAsync(7, enforcePrivacyFilter: true);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("not public"));
        await _videos.DidNotReceive().SaveAsync(Arg.Any<Video>());
    }

    [Test]
    public async Task ImportActivityAsync_PrivateActivityFilterDisabled_IsAccepted()
    {
        ArrangeValidTokens();
        _api.GetActivityAsync(Arg.Any<string>(), 7, Arg.Any<CancellationToken>())
            .Returns(OperationResult<StravaActivity>.Success(
                new StravaActivity
                {
                    Id = 7,
                    Visibility = "followers_only",
                    Type = "Run",
                    SportType = "Run",
                    StartDate = new DateTime(2026, 5, 15, 6, 0, 0, DateTimeKind.Utc),
                    MovingTimeSeconds = 1800,
                    DistanceMeters = 5000
                }));
        _videos.GetAllAsync().Returns(Array.Empty<Video>());
        _videos.GenerateNextId().Returns(99);

        var result = await _sync.ImportActivityAsync(7, enforcePrivacyFilter: false);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Is.Not.Null);
        Assert.That(result.Value!.Id, Is.EqualTo(99));
        Assert.That(result.Value.Training!.Source, Is.EqualTo(TrainingSource.Strava));
    }

    [Test]
    public async Task ImportActivityAsync_ExistingVideoWithSameExternalId_UpdatesInPlace()
    {
        ArrangeValidTokens();
        var existing = Video.Create(
            id: 5, title: "Old name", description: "", fileName: "video.mp4",
            location: null, category: VideoCategories.Running, fileSizeBytes: 100);
        existing.Training = new TrainingData
        {
            Source = TrainingSource.Strava,
            ExternalId = "7"
        };
        _videos.GetAllAsync().Returns(new[] { existing });

        _api.GetActivityAsync(Arg.Any<string>(), 7, Arg.Any<CancellationToken>())
            .Returns(OperationResult<StravaActivity>.Success(
                new StravaActivity
                {
                    Id = 7,
                    Visibility = "everyone",
                    Type = "Run",
                    SportType = "Run",
                    StartDate = new DateTime(2026, 5, 15, 6, 0, 0, DateTimeKind.Utc),
                    MovingTimeSeconds = 1800,
                    DistanceMeters = 6000,
                    AverageHeartRate = 160
                }));

        var result = await _sync.ImportActivityAsync(7);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Is.SameAs(existing));
        Assert.That(existing.Training!.DistanceMeters, Is.EqualTo(6000));
        Assert.That(existing.Training.AverageHeartRate, Is.EqualTo(160));
        await _videos.Received(1).SaveAsync(existing);
        _videos.DidNotReceive().GenerateNextId();
    }

    [Test]
    public async Task ImportActivityAsync_ActivityOwnedByMultiSportMaster_ReturnsMasterWithoutOverwrite()
    {
        // A previous merge swallowed Strava activity #7 into a multi-sport
        // master collection. The next sync round must NOT recreate a stump
        // for #7 nor overwrite the master's aggregate Training payload.
        ArrangeValidTokens();
        var master = Video.Create(
            id: 42, title: "Hybrid Sunday", description: "merged",
            fileName: "cover.jpg", location: null,
            category: "Multi-sport", fileSizeBytes: 0);
        master.Training = new TrainingData
        {
            Source = TrainingSource.Strava,
            ActivityType = "Multi-sport",
            ExternalId = string.Empty,
            Duration = TimeSpan.FromMinutes(75),
            Calories = 450,
            AverageHeartRate = 140,
            SubActivities = new List<SubActivityLink>
            {
                new(
                    Source: TrainingSource.Strava,
                    ExternalId: "7",
                    ExternalUrl: "https://www.strava.com/activities/7",
                    ActivityType: "Run",
                    StartTimeUtc: new DateTime(2026, 5, 15, 6, 0, 0, DateTimeKind.Utc),
                    Duration: TimeSpan.FromMinutes(30),
                    DistanceMeters: 5000,
                    Calories: 200,
                    AverageHeartRate: 150,
                    SufferScore: 50),
                new(
                    Source: TrainingSource.Strava,
                    ExternalId: "8",
                    ExternalUrl: "https://www.strava.com/activities/8",
                    ActivityType: "WeightTraining",
                    StartTimeUtc: new DateTime(2026, 5, 15, 13, 0, 0, DateTimeKind.Utc),
                    Duration: TimeSpan.FromMinutes(45),
                    DistanceMeters: null,
                    Calories: 250,
                    AverageHeartRate: 130,
                    SufferScore: 40),
            },
        };
        _videos.GetAllAsync().Returns(new[] { master });

        _api.GetActivityAsync(Arg.Any<string>(), 7, Arg.Any<CancellationToken>())
            .Returns(OperationResult<StravaActivity>.Success(
                new StravaActivity
                {
                    Id = 7,
                    Visibility = "everyone",
                    Type = "Run",
                    SportType = "Run",
                    StartDate = new DateTime(2026, 5, 15, 6, 0, 0, DateTimeKind.Utc),
                    MovingTimeSeconds = 1800,
                    DistanceMeters = 5000,
                }));

        var result = await _sync.ImportActivityAsync(7);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Is.SameAs(master));

        // The aggregate must be untouched — ActivityType still "Multi-sport",
        // Duration still 75 minutes, SubActivities[] still intact.
        Assert.That(master.Training!.ActivityType, Is.EqualTo("Multi-sport"));
        Assert.That(master.Training.Duration, Is.EqualTo(TimeSpan.FromMinutes(75)));
        Assert.That(master.Training.SubActivities!.Count, Is.EqualTo(2));
        await _videos.DidNotReceive().SaveAsync(master);
        _videos.DidNotReceive().GenerateNextId();
    }

    [Test]
    public async Task ImportActivityAsync_PublicActivityNew_PrefillsLocationAndGps()
    {
        ArrangeValidTokens();
        _api.GetActivityAsync(Arg.Any<string>(), 11, Arg.Any<CancellationToken>())
            .Returns(OperationResult<StravaActivity>.Success(
                new StravaActivity
                {
                    Id = 11,
                    Visibility = "everyone",
                    Type = "Hike",
                    SportType = "Hike",
                    StartDate = new DateTime(2026, 5, 15, 7, 0, 0, DateTimeKind.Utc),
                    MovingTimeSeconds = 3600,
                    DistanceMeters = 8000,
                    StartLatLng = new[] { 50.0614, 19.9366 },
                    LocationCity = "Krakow",
                    LocationCountry = "Poland"
                }));
        _videos.GetAllAsync().Returns(Array.Empty<Video>());
        _videos.GenerateNextId().Returns(123);

        var result = await _sync.ImportActivityAsync(11);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Latitude, Is.EqualTo(50.0614));
        Assert.That(result.Value.Longitude, Is.EqualTo(19.9366));
        Assert.That(result.Value.Location, Is.EqualTo("Krakow, Poland"));
        Assert.That(result.Value.Category, Is.EqualTo(VideoCategories.Gory));
        Assert.That(result.Value.HasCoordinates, Is.True);
    }

    [Test]
    public async Task AttachActivityToVideoAsync_VideoMissing_ReturnsFailure()
    {
        ArrangeValidTokens();
        _videos.GetByIdAsync(404).Returns((Video?)null);

        var result = await _sync.AttachActivityToVideoAsync(videoId: 404, activityId: 7);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("not found"));
        await _api.DidNotReceive().GetActivityAsync(
            Arg.Any<string>(), Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task AttachActivityToVideoAsync_FetchFails_LeavesVideoUntouched()
    {
        ArrangeValidTokens();
        var video = Video.Create(
            id: 12, title: "Existing", description: "", fileName: "v.mp4",
            location: null, category: VideoCategories.Running, fileSizeBytes: 0);
        _videos.GetByIdAsync(12).Returns(video);
        _api.GetActivityAsync(Arg.Any<string>(), 9, Arg.Any<CancellationToken>())
            .Returns(OperationResult<StravaActivity>.Failure("404 Not Found"));

        var result = await _sync.AttachActivityToVideoAsync(videoId: 12, activityId: 9);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("404"));
        Assert.That(video.Training, Is.Null);
        await _videos.DidNotReceive().SaveAsync(Arg.Any<Video>());
    }

    [Test]
    public async Task AttachActivityToVideoAsync_Success_AttachesTrainingDataAndSaves()
    {
        ArrangeValidTokens();
        var video = Video.Create(
            id: 33, title: "Solo run", description: "", fileName: "run.mp4",
            location: null, category: VideoCategories.Running, fileSizeBytes: 0);
        _videos.GetByIdAsync(33).Returns(video);

        _api.GetActivityAsync(Arg.Any<string>(), 88, Arg.Any<CancellationToken>())
            .Returns(OperationResult<StravaActivity>.Success(new StravaActivity
            {
                Id = 88,
                Type = "Run",
                SportType = "Run",
                StartDate = new DateTime(2026, 4, 2, 6, 0, 0, DateTimeKind.Utc),
                MovingTimeSeconds = 2400,
                DistanceMeters = 8000
            }));

        var result = await _sync.AttachActivityToVideoAsync(videoId: 33, activityId: 88);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Is.SameAs(video));
        Assert.That(video.Training, Is.Not.Null);
        Assert.That(video.Training!.Source, Is.EqualTo(TrainingSource.Strava));
        Assert.That(video.Training.ExternalId, Is.EqualTo("88"));
        Assert.That(video.Training.DistanceMeters, Is.EqualTo(8000));
        await _videos.Received(1).SaveAsync(video);
    }

    [Test]
    public async Task SyncRecentAsync_TokenMissing_PropagatesFailure()
    {
        _store.LoadAsync(Arg.Any<CancellationToken>()).Returns((StravaTokenSet?)null);

        var result = await _sync.SyncRecentAsync();

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("not been connected"));
    }

    [Test]
    public async Task SyncRecentAsync_DedupesByExternalIdAndCountsCategories()
    {
        ArrangeValidTokens();

        // One activity is already imported, one is brand-new.
        var existingVideo = Video.Create(
            id: 1, title: "Old", description: "", fileName: "old.mp4",
            location: null, category: VideoCategories.Running, fileSizeBytes: 0);
        existingVideo.Training = new TrainingData
        {
            Source = TrainingSource.Strava,
            ExternalId = "100"
        };
        _videos.GetAllAsync().Returns(new[] { existingVideo });
        _videos.GenerateNextId().Returns(200);

        _api.ListAthleteActivitiesAsync(
                Arg.Any<string>(), 1, 30, Arg.Any<CancellationToken>())
            .Returns(OperationResult<IReadOnlyList<StravaActivity>>.Success(new List<StravaActivity>
            {
                new() { Id = 100, Type = "Run", SportType = "Run", Visibility = "everyone" },
                new() { Id = 200, Type = "Run", SportType = "Run", Visibility = "everyone",
                    StartDate = new DateTime(2026, 5, 14, 6, 0, 0, DateTimeKind.Utc),
                    MovingTimeSeconds = 1800, DistanceMeters = 5000 }
            }));

        // Detail fetch for activity 200 succeeds.
        _api.GetActivityAsync(Arg.Any<string>(), 200, Arg.Any<CancellationToken>())
            .Returns(OperationResult<StravaActivity>.Success(new StravaActivity
            {
                Id = 200,
                Type = "Run",
                SportType = "Run",
                Visibility = "everyone",
                StartDate = new DateTime(2026, 5, 14, 6, 0, 0, DateTimeKind.Utc),
                MovingTimeSeconds = 1800,
                DistanceMeters = 5000
            }));

        var result = await _sync.SyncRecentAsync();

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Inspected, Is.EqualTo(2));
        Assert.That(result.Value.Imported, Is.EqualTo(1));
        Assert.That(result.Value.Skipped, Is.EqualTo(1));
        Assert.That(result.Value.Failed, Is.EqualTo(0));
        // Existing activity 100 must not be re-imported.
        await _api.DidNotReceive().GetActivityAsync(
            Arg.Any<string>(), 100, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SyncRecentAsync_PerActivityFailure_AccumulatesFailureMessages()
    {
        ArrangeValidTokens();
        _videos.GetAllAsync().Returns(Array.Empty<Video>());

        _api.ListAthleteActivitiesAsync(
                Arg.Any<string>(), 1, 30, Arg.Any<CancellationToken>())
            .Returns(OperationResult<IReadOnlyList<StravaActivity>>.Success(new List<StravaActivity>
            {
                new() { Id = 7, Type = "Run", SportType = "Run", Visibility = "everyone" }
            }));

        _api.GetActivityAsync(Arg.Any<string>(), 7, Arg.Any<CancellationToken>())
            .Returns(OperationResult<StravaActivity>.Failure("Strava API returned 500."));

        var result = await _sync.SyncRecentAsync();

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Inspected, Is.EqualTo(1));
        Assert.That(result.Value.Imported, Is.EqualTo(0));
        Assert.That(result.Value.Failed, Is.EqualTo(1));
        Assert.That(result.Value.FailureMessages, Has.Count.EqualTo(1));
        Assert.That(result.Value.FailureMessages[0], Does.Contain("7"));
        Assert.That(result.Value.FailureMessages[0], Does.Contain("500"));
    }

    [Test]
    public async Task SyncRecentAsync_NewActivities_AreImportedAsStumps()
    {
        // "Stump" = placeholder gallery item with training data + cover
        // image but no real user media. Verify the new item has no
        // user-uploaded media beyond the optional category cover.
        ArrangeValidTokens();
        _videos.GetAllAsync().Returns(Array.Empty<Video>());
        _videos.GenerateNextId().Returns(50);

        _api.ListAthleteActivitiesAsync(
                Arg.Any<string>(), 1, 30, Arg.Any<CancellationToken>())
            .Returns(OperationResult<IReadOnlyList<StravaActivity>>.Success(new List<StravaActivity>
            {
                new()
                {
                    Id = 555,
                    Type = "Run",
                    SportType = "Run",
                    Visibility = "everyone",
                    StartDate = new DateTime(2026, 5, 15, 6, 0, 0, DateTimeKind.Utc),
                    MovingTimeSeconds = 1800,
                    DistanceMeters = 5000
                }
            }));

        _api.GetActivityAsync(Arg.Any<string>(), 555, Arg.Any<CancellationToken>())
            .Returns(OperationResult<StravaActivity>.Success(new StravaActivity
            {
                Id = 555,
                Type = "Run",
                SportType = "Run",
                Visibility = "everyone",
                StartDate = new DateTime(2026, 5, 15, 6, 0, 0, DateTimeKind.Utc),
                MovingTimeSeconds = 1800,
                DistanceMeters = 5000
            }));

        Video? saved = null;
        await _videos.GetAllAsync(); // priming
        _videos.SaveAsync(Arg.Do<Video>(v => saved = v))
            .Returns(Task.CompletedTask);

        var result = await _sync.SyncRecentAsync();

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Imported, Is.EqualTo(1));
        Assert.That(saved, Is.Not.Null,
            "SyncRecentAsync should have saved exactly one new gallery item");
        Assert.That(saved!.Training, Is.Not.Null,
            "Stump must carry the Strava training metrics");
        Assert.That(saved.Training!.Source, Is.EqualTo(TrainingSource.Strava));
        Assert.That(saved.Training.ExternalId, Is.EqualTo("555"));
        // Cover-image seeding via IFileStorageService is mocked to return 0
        // in the SetUp, so the stump correctly has no media attached and is
        // ready for the operator to add real files later.
        Assert.That(saved.Media, Is.Empty);
        Assert.That(saved.FileName, Is.EqualTo(string.Empty));
    }

    [Test]
    public async Task SyncRecentAsync_TwoVisits_DedupesOnSecondPass()
    {
        // Auto-import is wired to the /admin/strava page entry, so re-
        // visiting the page must NOT create duplicate stumps for activities
        // already in the gallery.
        ArrangeValidTokens();

        _api.ListAthleteActivitiesAsync(
                Arg.Any<string>(), 1, 30, Arg.Any<CancellationToken>())
            .Returns(OperationResult<IReadOnlyList<StravaActivity>>.Success(new List<StravaActivity>
            {
                new() { Id = 101, Type = "Run", SportType = "Run", Visibility = "everyone" }
            }));

        _api.GetActivityAsync(Arg.Any<string>(), 101, Arg.Any<CancellationToken>())
            .Returns(OperationResult<StravaActivity>.Success(new StravaActivity
            {
                Id = 101,
                Type = "Run",
                SportType = "Run",
                Visibility = "everyone",
                StartDate = new DateTime(2026, 5, 15, 6, 0, 0, DateTimeKind.Utc),
                MovingTimeSeconds = 1800,
                DistanceMeters = 5000
            }));

        // First pass: no existing items.
        _videos.GetAllAsync().Returns(Array.Empty<Video>());
        _videos.GenerateNextId().Returns(60);
        var firstResult = await _sync.SyncRecentAsync();

        Assert.That(firstResult.Value!.Imported, Is.EqualTo(1));
        Assert.That(firstResult.Value.Skipped, Is.EqualTo(0));

        // Second pass: pretend the import landed in the gallery.
        var existing = Video.Create(
            id: 60, title: "Already there", description: "", fileName: "",
            location: null, category: VideoCategories.Running, fileSizeBytes: 0);
        existing.Training = new TrainingData
        {
            Source = TrainingSource.Strava,
            ExternalId = "101"
        };
        _videos.GetAllAsync().Returns(new[] { existing });

        var secondResult = await _sync.SyncRecentAsync();

        Assert.That(secondResult.Value!.Imported, Is.EqualTo(0),
            "Activity 101 must not be re-imported on the second page visit");
        Assert.That(secondResult.Value.Skipped, Is.EqualTo(1));
    }

    [Test]
    public async Task ImportActivityAsync_ConcurrentCalls_OnlyOneSavesNewVideo()
    {
        // Two callers racing on the same activity must produce exactly one
        // gallery item — the ImportLock serialises them and FindExistingAsync
        // is re-checked inside the lock so the second call sees the first
        // call's save and updates the same video instead of creating a dupe.
        ArrangeValidTokens();
        _api.GetActivityAsync(Arg.Any<string>(), 555, Arg.Any<CancellationToken>())
            .Returns(OperationResult<StravaActivity>.Success(new StravaActivity
            {
                Id = 555,
                Type = "Run",
                SportType = "Run",
                Visibility = "everyone",
                StartDate = new DateTime(2026, 5, 15, 6, 0, 0, DateTimeKind.Utc),
                MovingTimeSeconds = 1800,
                DistanceMeters = 5000
            }));

        // GetAllAsync returns whatever was last saved — the substitute
        // tracks the saved video so the second concurrent caller sees it.
        var savedVideos = new List<Video>();
        _videos.GetAllAsync().Returns(_ => savedVideos.AsReadOnly());
        _videos.GenerateNextId().Returns(70);
        _videos.SaveAsync(Arg.Do<Video>(v =>
        {
            // Track first save so subsequent GetAllAsync calls reflect it.
            if (savedVideos.All(s => s.Id != v.Id))
                savedVideos.Add(v);
        })).Returns(Task.CompletedTask);

        var taskA = _sync.ImportActivityAsync(555, enforcePrivacyFilter: false);
        var taskB = _sync.ImportActivityAsync(555, enforcePrivacyFilter: false);
        var results = await Task.WhenAll(taskA, taskB);

        Assert.That(results[0].IsSuccess, Is.True);
        Assert.That(results[1].IsSuccess, Is.True);
        // Both calls return success but only one new gallery item exists.
        Assert.That(savedVideos.Select(v => v.Id).Distinct().ToArray(),
            Is.EqualTo(new[] { 70 }));
        // GenerateNextId must have been called exactly once — the second
        // caller short-circuited via FindExistingAsync.
        _videos.Received(1).GenerateNextId();
    }

    [Test]
    public async Task ImportActivityAsync_DuplicateStumps_CollapsedToCanonical()
    {
        // Pre-existing duplicates (from before the ImportLock fix shipped)
        // must be collapsed automatically on the next import. Pure stumps
        // — items without user-added media — are safe to delete.
        ArrangeValidTokens();

        var dupeOlder = MakeStravaStump(id: 10, externalId: "555");
        var dupeNewer = MakeStravaStump(id: 12, externalId: "555");
        var dupeNewest = MakeStravaStump(id: 15, externalId: "555");
        _videos.GetAllAsync().Returns(new[] { dupeOlder, dupeNewer, dupeNewest });

        _api.GetActivityAsync(Arg.Any<string>(), 555, Arg.Any<CancellationToken>())
            .Returns(OperationResult<StravaActivity>.Success(new StravaActivity
            {
                Id = 555,
                Type = "Run",
                SportType = "Run",
                Visibility = "everyone",
                StartDate = new DateTime(2026, 5, 15, 6, 0, 0, DateTimeKind.Utc),
                MovingTimeSeconds = 1800,
                DistanceMeters = 5000
            }));

        var result = await _sync.ImportActivityAsync(555);

        Assert.That(result.IsSuccess, Is.True);
        // Canonical = lowest id (oldest record).
        Assert.That(result.Value!.Id, Is.EqualTo(10));
        // The other two stumps were deleted.
        await _videos.Received(1).DeleteAsync(12);
        await _videos.Received(1).DeleteAsync(15);
        await _videos.DidNotReceive().DeleteAsync(10);
        // Canonical was re-saved with refreshed training data.
        await _videos.Received(1).SaveAsync(Arg.Is<Video>(v => v.Id == 10));
    }

    [Test]
    public async Task ImportActivityAsync_DuplicateWithUserMedia_PicksTouchedAsCanonical()
    {
        // A duplicate that the operator has been working on (real photos /
        // videos beyond the cover.jpg seed) must be promoted to canonical
        // and pure stumps get collapsed into it — never the other way
        // around, so operator work is preserved.
        ArrangeValidTokens();

        var stump = MakeStravaStump(id: 10, externalId: "555");
        var touched = MakeStravaStump(id: 12, externalId: "555");
        touched.Media = new List<MediaItem>
        {
            MediaItem.Create("photo-01.jpg", MediaType.Image, 200, 0),
            MediaItem.Create("video.mp4", MediaType.Video, 5_000_000, 1)
        };
        _videos.GetAllAsync().Returns(new[] { stump, touched });

        _api.GetActivityAsync(Arg.Any<string>(), 555, Arg.Any<CancellationToken>())
            .Returns(OperationResult<StravaActivity>.Success(new StravaActivity
            {
                Id = 555,
                Type = "Run",
                SportType = "Run",
                Visibility = "everyone"
            }));

        var result = await _sync.ImportActivityAsync(555);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Id, Is.EqualTo(12),
            "Canonical should be the item with user-added media");
        await _videos.Received(1).DeleteAsync(10);
        await _videos.DidNotReceive().DeleteAsync(12);
    }

    [Test]
    public async Task ImportActivityAsync_BothDuplicatesHaveUserMedia_NeitherDeleted()
    {
        // When both duplicates carry real media, leave them alone for
        // manual merge — better to leak a dupe than silently delete
        // operator work.
        ArrangeValidTokens();

        var olderTouched = MakeStravaStump(id: 10, externalId: "555");
        olderTouched.Media = new List<MediaItem>
        {
            MediaItem.Create("photo-old.jpg", MediaType.Image, 100, 0)
        };
        var newerTouched = MakeStravaStump(id: 12, externalId: "555");
        newerTouched.Media = new List<MediaItem>
        {
            MediaItem.Create("photo-new.jpg", MediaType.Image, 200, 0),
            MediaItem.Create("video.mp4", MediaType.Video, 5_000_000, 1)
        };
        _videos.GetAllAsync().Returns(new[] { olderTouched, newerTouched });

        _api.GetActivityAsync(Arg.Any<string>(), 555, Arg.Any<CancellationToken>())
            .Returns(OperationResult<StravaActivity>.Success(new StravaActivity
            {
                Id = 555,
                Type = "Run",
                SportType = "Run",
                Visibility = "everyone"
            }));

        var result = await _sync.ImportActivityAsync(555);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Id, Is.EqualTo(12),
            "Canonical picks the more-developed item");
        await _videos.DidNotReceive().DeleteAsync(Arg.Any<int>());
    }

    [Test]
    public async Task ImportActivityAsync_ExistingStumpWithStaleCategory_IsMigrated()
    {
        // Old Strava ride imported before the Bicycle category existed
        // would carry Category=Running. Re-running the import should
        // migrate it to Bicycle, since the mapper now routes Ride there
        // and the stump has no user media to preserve.
        ArrangeValidTokens();

        var stale = Video.Create(
            id: 30, title: "Morning Ride", description: "", fileName: "cover.jpg",
            location: null, category: VideoCategories.Running, fileSizeBytes: 100);
        stale.Training = new TrainingData
        {
            Source = TrainingSource.Strava,
            ExternalId = "888",
            ActivityType = "Ride"
        };
        stale.Media = new List<MediaItem>
        {
            MediaItem.Create("cover.jpg", MediaType.Image, 100, 0)
        };
        _videos.GetAllAsync().Returns(new[] { stale });

        // The cover-seeding helper returns a non-zero byte count so the
        // stump still has a primary file after the migration.
        _storage.CopyWwwRootFileToVideoAsync(
                Arg.Any<string>(), 30, "cover.jpg")
            .Returns(200L);

        _api.GetActivityAsync(Arg.Any<string>(), 888, Arg.Any<CancellationToken>())
            .Returns(OperationResult<StravaActivity>.Success(new StravaActivity
            {
                Id = 888,
                Type = "Ride",
                SportType = "Ride",
                Visibility = "everyone"
            }));

        var result = await _sync.ImportActivityAsync(888);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(stale.Category, Is.EqualTo(VideoCategories.Bicycle));
        Assert.That(stale.Media, Has.Count.EqualTo(1));
        Assert.That(stale.Media[0].FileName, Is.EqualTo("cover.jpg"));
        Assert.That(stale.Media[0].SizeBytes, Is.EqualTo(200));
        Assert.That(stale.FileSizeBytes, Is.EqualTo(200));
        // Cover was re-seeded from the Bicycle bg asset.
        await _storage.Received().CopyWwwRootFileToVideoAsync(
            Arg.Is<string>(p => p.Contains("cycling-bg")), 30, "cover.jpg");
    }

    [Test]
    public async Task ImportActivityAsync_ExistingStumpWithCorrectCategoryButStaleCover_ReseedsCover()
    {
        // Symptom from the field: a Bicycle stump was showing the running
        // silhouette because the operator manually moved it Running ->
        // Bicycle in the editor. Category already matched the mapper on
        // the next sync, so the previous version of MigrateCategoryIfNeeded
        // skipped before the cover got re-seeded. The fix re-seeds the
        // cover unconditionally for pure stumps.
        ArrangeValidTokens();

        var stump = Video.Create(
            id: 50, title: "Lunch Ride", description: "", fileName: "cover.jpg",
            location: null, category: VideoCategories.Bicycle, fileSizeBytes: 100);
        stump.Training = new TrainingData
        {
            Source = TrainingSource.Strava,
            ExternalId = "999",
            ActivityType = "Ride"
        };
        stump.Media = new List<MediaItem>
        {
            MediaItem.Create("cover.jpg", MediaType.Image, 100, 0)
        };
        _videos.GetAllAsync().Returns(new[] { stump });

        _storage.CopyWwwRootFileToVideoAsync(
                Arg.Any<string>(), 50, "cover.jpg")
            .Returns(220L);

        _api.GetActivityAsync(Arg.Any<string>(), 999, Arg.Any<CancellationToken>())
            .Returns(OperationResult<StravaActivity>.Success(new StravaActivity
            {
                Id = 999,
                Type = "Ride",
                SportType = "Ride",
                Visibility = "everyone"
            }));

        var result = await _sync.ImportActivityAsync(999);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(stump.Category, Is.EqualTo(VideoCategories.Bicycle));
        // The cover was re-seeded from the Bicycle bg even though the
        // category did not change this round.
        await _storage.Received().CopyWwwRootFileToVideoAsync(
            Arg.Is<string>(p => p.Contains("cycling-bg")), 50, "cover.jpg");
        Assert.That(stump.FileSizeBytes, Is.EqualTo(220));
    }

    [Test]
    public async Task ImportActivityAsync_ExistingStumpWithUserMedia_CategoryNotMigrated()
    {
        // If the operator already added real media, leave the category
        // alone — moving it would invalidate whatever they curated.
        ArrangeValidTokens();

        var touched = Video.Create(
            id: 31, title: "Morning Ride", description: "", fileName: "photo-01.jpg",
            location: null, category: VideoCategories.Running, fileSizeBytes: 500);
        touched.Training = new TrainingData
        {
            Source = TrainingSource.Strava,
            ExternalId = "888",
            ActivityType = "Ride"
        };
        touched.Media = new List<MediaItem>
        {
            MediaItem.Create("photo-01.jpg", MediaType.Image, 500, 0)
        };
        _videos.GetAllAsync().Returns(new[] { touched });

        _api.GetActivityAsync(Arg.Any<string>(), 888, Arg.Any<CancellationToken>())
            .Returns(OperationResult<StravaActivity>.Success(new StravaActivity
            {
                Id = 888,
                Type = "Ride",
                SportType = "Ride",
                Visibility = "everyone"
            }));

        var result = await _sync.ImportActivityAsync(888);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(touched.Category, Is.EqualTo(VideoCategories.Running),
            "Touched items must keep their existing category");
        await _storage.DidNotReceive().CopyWwwRootFileToVideoAsync(
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>());
    }

    [Test]
    public async Task RefreshStumpCoversAsync_ReseedsCoverForEveryStumpUsingItsCategory()
    {
        // Each stored item picks its own bg asset based on its Category,
        // independent of the most-recent Strava activity payload.
        var bicycleStump = MakeStravaStump(id: 60, externalId: "111");
        bicycleStump.Category = VideoCategories.Bicycle;

        var runningStump = MakeStravaStump(id: 61, externalId: "222");
        runningStump.Category = VideoCategories.Running;

        var touched = MakeStravaStump(id: 62, externalId: "333");
        touched.Category = VideoCategories.Bicycle;
        touched.Media = new List<MediaItem>
        {
            MediaItem.Create("cover.jpg", MediaType.Image, 100, 0),
            MediaItem.Create("photo-01.jpg", MediaType.Image, 500, 1)
        };

        var nonStrava = Video.Create(
            id: 63, title: "Manual", description: "", fileName: "v.mp4",
            location: null, category: VideoCategories.Running, fileSizeBytes: 0);

        _videos.GetAllAsync().Returns(new[] { bicycleStump, runningStump, touched, nonStrava });
        // Override the SetUp's default-zero so the seed actually counts.
        _storage.CopyWwwRootFileToVideoAsync(
                Arg.Is<string>(p => p.Contains("cycling-bg")),
                Arg.Any<int>(),
                Arg.Any<string>())
            .Returns(220L);
        _storage.CopyWwwRootFileToVideoAsync(
                Arg.Is<string>(p => p.Contains("running-bg")),
                Arg.Any<int>(),
                Arg.Any<string>())
            .Returns(180L);

        var refreshed = await _sync.RefreshStumpCoversAsync();

        Assert.That(refreshed, Is.EqualTo(2),
            "Only the two pure stumps should be re-seeded");

        await _storage.Received().CopyWwwRootFileToVideoAsync(
            Arg.Is<string>(p => p.Contains("cycling-bg")), 60, "cover.jpg");
        await _storage.Received().CopyWwwRootFileToVideoAsync(
            Arg.Is<string>(p => p.Contains("running-bg")), 61, "cover.jpg");
        // Touched item and non-Strava item must not be touched.
        await _storage.DidNotReceive().CopyWwwRootFileToVideoAsync(
            Arg.Any<string>(), 62, Arg.Any<string>());
        await _storage.DidNotReceive().CopyWwwRootFileToVideoAsync(
            Arg.Any<string>(), 63, Arg.Any<string>());

        Assert.That(bicycleStump.FileSizeBytes, Is.EqualTo(220));
        Assert.That(runningStump.FileSizeBytes, Is.EqualTo(180));
    }

    private static Video MakeStravaStump(int id, string externalId)
    {
        var v = Video.Create(
            id: id, title: $"Strava {externalId}", description: "",
            fileName: "cover.jpg", location: null,
            category: VideoCategories.Running, fileSizeBytes: 0);
        v.Training = new TrainingData
        {
            Source = TrainingSource.Strava,
            ExternalId = externalId,
            ActivityType = "Run"
        };
        v.Media = new List<MediaItem>
        {
            MediaItem.Create("cover.jpg", MediaType.Image, 0, 0)
        };
        return v;
    }

    [Test]
    public async Task SyncRecentAsync_PrivacyFilterEnforced_PrivateActivitiesCountedAsFailures()
    {
        ArrangeValidTokens();
        _options.ImportPublicOnly = true;
        _videos.GetAllAsync().Returns(Array.Empty<Video>());

        _api.ListAthleteActivitiesAsync(
                Arg.Any<string>(), 1, 30, Arg.Any<CancellationToken>())
            .Returns(OperationResult<IReadOnlyList<StravaActivity>>.Success(new List<StravaActivity>
            {
                new() { Id = 11, Type = "Run", SportType = "Run", Visibility = "only_me" }
            }));

        _api.GetActivityAsync(Arg.Any<string>(), 11, Arg.Any<CancellationToken>())
            .Returns(OperationResult<StravaActivity>.Success(new StravaActivity
            {
                Id = 11,
                Type = "Run",
                SportType = "Run",
                Visibility = "only_me"
            }));

        var result = await _sync.SyncRecentAsync(perPage: 30, enforcePrivacyFilter: true);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Imported, Is.EqualTo(0));
        Assert.That(result.Value.Failed, Is.EqualTo(1));
        Assert.That(result.Value.FailureMessages[0], Does.Contain("not public"));
    }

    private void ArrangeValidTokens()
    {
        _store.LoadAsync(Arg.Any<CancellationToken>()).Returns(new StravaTokenSet
        {
            AthleteId = 1,
            AccessToken = "valid-token",
            RefreshToken = "refresh",
            ExpiresAtUtc = DateTime.UtcNow.AddHours(1),
            Scope = "read"
        });
    }
}
