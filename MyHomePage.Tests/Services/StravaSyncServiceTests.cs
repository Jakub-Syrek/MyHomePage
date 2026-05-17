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
