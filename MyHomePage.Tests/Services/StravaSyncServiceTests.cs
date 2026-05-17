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
