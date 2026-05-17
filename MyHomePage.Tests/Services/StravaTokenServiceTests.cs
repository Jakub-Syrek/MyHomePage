namespace MyHomePage.Tests.Services;

/// <summary>
/// Unit tests for <see cref="StravaTokenService"/>. The transport
/// (<see cref="IStravaApiClient"/>) and persistence
/// (<see cref="IStravaTokenStore"/>) are mocked so the tests focus on the
/// token lifecycle: exchange, refresh-on-expiry, disconnect.
/// </summary>
[TestFixture]
public sealed class StravaTokenServiceTests
{
    private IStravaApiClient _api = null!;
    private IStravaTokenStore _store = null!;
    private StravaTokenService _service = null!;

    [SetUp]
    public void Setup()
    {
        _api = Substitute.For<IStravaApiClient>();
        _store = Substitute.For<IStravaTokenStore>();
        var logger = Substitute.For<ILogger<StravaTokenService>>();
        _service = new StravaTokenService(_api, _store, logger);
    }

    [Test]
    public async Task CompleteAuthorizationAsync_EmptyCode_ReturnsFailureWithoutCallingApi()
    {
        var result = await _service.CompleteAuthorizationAsync("   ");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("Authorization code"));
        await _api.DidNotReceive().ExchangeCodeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _store.DidNotReceive().SaveAsync(Arg.Any<StravaTokenSet>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CompleteAuthorizationAsync_ExchangeFails_PropagatesMessage()
    {
        _api.ExchangeCodeAsync("code-123", Arg.Any<CancellationToken>())
            .Returns(OperationResult<StravaTokenResponse>.Failure("Strava rejected the code."));

        var result = await _service.CompleteAuthorizationAsync("code-123");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("rejected"));
        await _store.DidNotReceive().SaveAsync(Arg.Any<StravaTokenSet>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CompleteAuthorizationAsync_Success_PersistsTokensAndReturnsThem()
    {
        var expiresAtUnix = DateTimeOffset.UtcNow.AddHours(6).ToUnixTimeSeconds();
        _api.ExchangeCodeAsync("code", Arg.Any<CancellationToken>())
            .Returns(OperationResult<StravaTokenResponse>.Success(new StravaTokenResponse
            {
                AccessToken = "access",
                RefreshToken = "refresh",
                ExpiresAt = expiresAtUnix,
                Scope = "read,activity:read_all",
                Athlete = new StravaAthlete { Id = 555 }
            }));

        var result = await _service.CompleteAuthorizationAsync("code");

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.AthleteId, Is.EqualTo(555));
        Assert.That(result.Value.AccessToken, Is.EqualTo("access"));
        Assert.That(result.Value.RefreshToken, Is.EqualTo("refresh"));
        Assert.That(result.Value.Scope, Is.EqualTo("read,activity:read_all"));
        await _store.Received(1).SaveAsync(
            Arg.Is<StravaTokenSet>(t => t.AthleteId == 555 && t.AccessToken == "access"),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetValidAccessTokenAsync_NoTokens_ReturnsFailure()
    {
        _store.LoadAsync(Arg.Any<CancellationToken>()).Returns((StravaTokenSet?)null);

        var result = await _service.GetValidAccessTokenAsync();

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("not been connected"));
        await _api.DidNotReceive().RefreshTokenAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetValidAccessTokenAsync_FreshToken_ReturnsItWithoutRefresh()
    {
        _store.LoadAsync(Arg.Any<CancellationToken>()).Returns(new StravaTokenSet
        {
            AthleteId = 1,
            AccessToken = "still-valid",
            RefreshToken = "rt",
            ExpiresAtUtc = DateTime.UtcNow.AddHours(2),
            Scope = "read"
        });

        var result = await _service.GetValidAccessTokenAsync();

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Is.EqualTo("still-valid"));
        await _api.DidNotReceive().RefreshTokenAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetValidAccessTokenAsync_ExpiredToken_RefreshesAndPersistsNewSet()
    {
        var expired = new StravaTokenSet
        {
            AthleteId = 42,
            AccessToken = "stale",
            RefreshToken = "rt-stable",
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-5),
            Scope = "read"
        };
        _store.LoadAsync(Arg.Any<CancellationToken>()).Returns(expired);

        var refreshedUnix = DateTimeOffset.UtcNow.AddHours(6).ToUnixTimeSeconds();
        _api.RefreshTokenAsync("rt-stable", Arg.Any<CancellationToken>())
            .Returns(OperationResult<StravaTokenResponse>.Success(new StravaTokenResponse
            {
                AccessToken = "fresh",
                RefreshToken = "rt-stable",
                ExpiresAt = refreshedUnix,
                Scope = string.Empty // empty -> keep previous scope
            }));

        var result = await _service.GetValidAccessTokenAsync();

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Is.EqualTo("fresh"));
        await _store.Received(1).SaveAsync(
            Arg.Is<StravaTokenSet>(t =>
                t.AthleteId == 42 &&
                t.AccessToken == "fresh" &&
                t.RefreshToken == "rt-stable" &&
                t.Scope == "read"),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetValidAccessTokenAsync_ExpiredToken_EmptyRefreshToken_FailsWithoutCallingApi()
    {
        _store.LoadAsync(Arg.Any<CancellationToken>()).Returns(new StravaTokenSet
        {
            AthleteId = 1,
            AccessToken = "stale",
            RefreshToken = string.Empty,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-5),
            Scope = "read"
        });

        var result = await _service.GetValidAccessTokenAsync();

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("refresh token"));
        await _api.DidNotReceive().RefreshTokenAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetValidAccessTokenAsync_RefreshApiFails_PropagatesMessage()
    {
        _store.LoadAsync(Arg.Any<CancellationToken>()).Returns(new StravaTokenSet
        {
            AthleteId = 1,
            AccessToken = "stale",
            RefreshToken = "rt",
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-5),
            Scope = "read"
        });
        _api.RefreshTokenAsync("rt", Arg.Any<CancellationToken>())
            .Returns(OperationResult<StravaTokenResponse>.Failure("Strava 401 Unauthorized"));

        var result = await _service.GetValidAccessTokenAsync();

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("401"));
        await _store.DidNotReceive().SaveAsync(Arg.Any<StravaTokenSet>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DisconnectAsync_DelegatesToStoreDelete()
    {
        var result = await _service.DisconnectAsync();

        Assert.That(result.IsSuccess, Is.True);
        await _store.Received(1).DeleteAsync(Arg.Any<CancellationToken>());
    }
}
