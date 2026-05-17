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
