using System.Net;
using System.Net.Http;
using System.Text;

namespace MyHomePage.Tests.Services;

/// <summary>
/// Tests for the HTTP-using methods on <see cref="StravaApiClient"/>. The
/// transport is stubbed with a fake <see cref="HttpMessageHandler"/> so the
/// request the client builds and the response it parses can both be
/// verified deterministically without hitting the real Strava API.
/// </summary>
[TestFixture]
public sealed class StravaApiClientHttpTests
{
    /// <summary>
    /// Recording handler: lets each test see exactly what request the
    /// client produced (URL, headers, body) and pin the response it
    /// should react to. Body content is captured into a separate string
    /// because StravaApiClient disposes the FormUrlEncodedContent before
    /// returning, so the original HttpRequestMessage.Content is unreadable
    /// after the call.
    /// </summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = new();
        public List<string?> RequestBodies { get; } = new();
        public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } =
            _ => new HttpResponseMessage(HttpStatusCode.OK);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string? body = null;
            if (request.Content is not null)
                body = await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(request);
            RequestBodies.Add(body);
            return Respond(request);
        }
    }

    private static StravaApiClient MakeClient(RecordingHandler handler)
    {
        var http = new HttpClient(handler);
        var options = Microsoft.Extensions.Options.Options.Create(new StravaOptions
        {
            ClientId = "test-client",
            ClientSecret = "test-secret",
            RedirectUri = "https://example.com/cb",
            Scope = "read"
        });
        var logger = Substitute.For<ILogger<StravaApiClient>>();
        return new StravaApiClient(http, options, logger);
    }

    private static HttpResponseMessage JsonOk(string body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    // ── ExchangeCodeAsync ─────────────────────────────────────────────────

    [Test]
    public async Task ExchangeCodeAsync_PostsClientCredentialsAndAuthCode()
    {
        var handler = new RecordingHandler
        {
            Respond = _ => JsonOk("""
            {
                "token_type": "Bearer",
                "access_token": "AT",
                "refresh_token": "RT",
                "expires_at": 1900000000,
                "scope": "read",
                "athlete": { "id": 42 }
            }
            """)
        };
        var client = MakeClient(handler);

        var result = await client.ExchangeCodeAsync("the-code");

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.AccessToken, Is.EqualTo("AT"));
        Assert.That(result.Value.RefreshToken, Is.EqualTo("RT"));
        Assert.That(result.Value.Athlete!.Id, Is.EqualTo(42));

        Assert.That(handler.Requests, Has.Count.EqualTo(1));
        var sent = handler.Requests[0];
        Assert.That(sent.Method, Is.EqualTo(HttpMethod.Post));
        Assert.That(sent.RequestUri!.ToString(), Is.EqualTo("https://www.strava.com/oauth/token"));
        var body = handler.RequestBodies[0];
        Assert.That(body, Does.Contain("client_id=test-client"));
        Assert.That(body, Does.Contain("client_secret=test-secret"));
        Assert.That(body, Does.Contain("code=the-code"));
        Assert.That(body, Does.Contain("grant_type=authorization_code"));
    }

    [Test]
    public async Task ExchangeCodeAsync_NonSuccessStatus_ReturnsFailureWithStatusCode()
    {
        var handler = new RecordingHandler
        {
            Respond = _ => new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("{\"error\":\"invalid_code\"}",
                    Encoding.UTF8, "application/json"),
                ReasonPhrase = "Bad Request"
            }
        };
        var client = MakeClient(handler);

        var result = await client.ExchangeCodeAsync("bad-code");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("400"));
    }

    [Test]
    public async Task ExchangeCodeAsync_EmptyJsonBody_ReturnsFailure()
    {
        // ReadFromJsonAsync of literal "null" → null body → branch fired.
        var handler = new RecordingHandler { Respond = _ => JsonOk("null") };
        var client = MakeClient(handler);

        var result = await client.ExchangeCodeAsync("code");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("empty token body"));
    }

    [Test]
    public async Task ExchangeCodeAsync_NetworkException_ReturnsFailure()
    {
        var handler = new ThrowingHandler(new HttpRequestException("DNS down"));
        var client = MakeClient(new RecordingHandler()); // not used
        // We rebuild with the throwing handler:
        var http = new HttpClient(handler);
        var options = Microsoft.Extensions.Options.Options.Create(new StravaOptions
        {
            ClientId = "c", ClientSecret = "s"
        });
        var logger = Substitute.For<ILogger<StravaApiClient>>();
        client = new StravaApiClient(http, options, logger);

        var result = await client.ExchangeCodeAsync("code");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("DNS down"));
    }

    // ── RefreshTokenAsync ─────────────────────────────────────────────────

    [Test]
    public async Task RefreshTokenAsync_PostsRefreshGrant()
    {
        var handler = new RecordingHandler
        {
            Respond = _ => JsonOk("""
            { "access_token": "new-AT", "refresh_token": "new-RT",
              "expires_at": 1900000000, "scope": "read,activity:read_all" }
            """)
        };
        var client = MakeClient(handler);

        var result = await client.RefreshTokenAsync("old-RT");

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.AccessToken, Is.EqualTo("new-AT"));
        var body = handler.RequestBodies[0];
        Assert.That(body, Does.Contain("grant_type=refresh_token"));
        Assert.That(body, Does.Contain("refresh_token=old-RT"));
    }

    // ── GetActivityAsync ──────────────────────────────────────────────────

    [Test]
    public async Task GetActivityAsync_AttachesBearerHeaderAndParsesActivity()
    {
        var handler = new RecordingHandler
        {
            Respond = _ => JsonOk("""
            {
                "id": 12345,
                "name": "Morning Run",
                "type": "Run",
                "sport_type": "Run",
                "start_date": "2026-05-15T06:00:00Z",
                "moving_time": 1800,
                "distance": 5000,
                "average_heartrate": 150,
                "visibility": "everyone",
                "splits_metric": [
                    { "split": 1, "distance": 1000, "moving_time": 360,
                      "elapsed_time": 360, "average_speed": 2.78, "pace_zone": 2 }
                ]
            }
            """)
        };
        var client = MakeClient(handler);

        var result = await client.GetActivityAsync("bearer-xyz", 12345);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Id, Is.EqualTo(12345));
        Assert.That(result.Value.SportType, Is.EqualTo("Run"));
        Assert.That(result.Value.AverageHeartRate, Is.EqualTo(150));
        Assert.That(result.Value.SplitsMetric, Has.Count.EqualTo(1));
        Assert.That(result.Value.SplitsMetric![0].DistanceMeters, Is.EqualTo(1000));

        var sent = handler.Requests[0];
        Assert.That(sent.RequestUri!.ToString(),
            Does.Contain("/activities/12345"));
        Assert.That(sent.Headers.Authorization!.Scheme, Is.EqualTo("Bearer"));
        Assert.That(sent.Headers.Authorization.Parameter, Is.EqualTo("bearer-xyz"));
    }

    [Test]
    public async Task GetActivityAsync_404_ReturnsFailureWithStatus()
    {
        var handler = new RecordingHandler
        {
            Respond = _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("{\"message\":\"Resource Not Found\"}"),
                ReasonPhrase = "Not Found"
            }
        };
        var client = MakeClient(handler);

        var result = await client.GetActivityAsync("at", 7);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("404"));
    }

    [Test]
    public async Task GetActivityAsync_EmptyBody_ReturnsFailure()
    {
        var handler = new RecordingHandler { Respond = _ => JsonOk("null") };
        var client = MakeClient(handler);

        var result = await client.GetActivityAsync("at", 1);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("empty activity"));
    }

    // ── GetGearAsync ──────────────────────────────────────────────────────

    [Test]
    public async Task GetGearAsync_EmptyGearId_ReturnsFailureWithoutHttpCall()
    {
        var handler = new RecordingHandler();
        var client = MakeClient(handler);

        var result = await client.GetGearAsync("at", "");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("Gear id"));
        Assert.That(handler.Requests, Is.Empty);
    }

    [Test]
    public async Task GetGearAsync_ParsesGearAndUrlEncodesId()
    {
        var handler = new RecordingHandler
        {
            Respond = _ => JsonOk("""
            { "id": "g123", "nickname": "Daily trainer",
              "brand_name": "Hoka", "model_name": "Clifton 9" }
            """)
        };
        var client = MakeClient(handler);

        var result = await client.GetGearAsync("at", "g 123");

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Nickname, Is.EqualTo("Daily trainer"));
        Assert.That(result.Value.BrandName, Is.EqualTo("Hoka"));
        Assert.That(handler.Requests[0].RequestUri!.AbsoluteUri,
            Does.Contain("/gear/g%20123"));
    }

    [Test]
    public async Task GetGearAsync_NonSuccessStatus_ReturnsFailure()
    {
        var handler = new RecordingHandler
        {
            Respond = _ => new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent("no"),
                ReasonPhrase = "Forbidden"
            }
        };
        var client = MakeClient(handler);

        var result = await client.GetGearAsync("at", "g1");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("403"));
    }

    [Test]
    public async Task GetGearAsync_NetworkException_ReturnsFailure()
    {
        var handler = new ThrowingHandler(new HttpRequestException("connection refused"));
        var http = new HttpClient(handler);
        var options = Microsoft.Extensions.Options.Options.Create(new StravaOptions());
        var logger = Substitute.For<ILogger<StravaApiClient>>();
        var client = new StravaApiClient(http, options, logger);

        var result = await client.GetGearAsync("at", "g1");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("connection refused"));
    }

    // ── ListAthleteActivitiesAsync ────────────────────────────────────────

    [Test]
    public async Task ListAthleteActivitiesAsync_BuildsCorrectQueryAndParsesArray()
    {
        var handler = new RecordingHandler
        {
            Respond = _ => JsonOk("""
            [ { "id": 1, "type": "Run", "name": "A" },
              { "id": 2, "type": "Hike", "name": "B" } ]
            """)
        };
        var client = MakeClient(handler);

        var result = await client.ListAthleteActivitiesAsync("at", page: 2, perPage: 10);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Has.Count.EqualTo(2));
        Assert.That(result.Value![0].Id, Is.EqualTo(1));
        Assert.That(handler.Requests[0].RequestUri!.Query,
            Is.EqualTo("?page=2&per_page=10"));
    }

    [Test]
    public async Task ListAthleteActivitiesAsync_PerPageOutOfRange_ClampsTo30()
    {
        var handler = new RecordingHandler { Respond = _ => JsonOk("[]") };
        var client = MakeClient(handler);

        await client.ListAthleteActivitiesAsync("at", page: 1, perPage: 999);

        Assert.That(handler.Requests[0].RequestUri!.Query, Does.Contain("per_page=30"));
    }

    [Test]
    public async Task ListAthleteActivitiesAsync_PerPageBelowOne_ClampsToOne()
    {
        var handler = new RecordingHandler { Respond = _ => JsonOk("[]") };
        var client = MakeClient(handler);

        await client.ListAthleteActivitiesAsync("at", page: 1, perPage: 0);

        Assert.That(handler.Requests[0].RequestUri!.Query, Does.Contain("per_page=1"));
    }

    [Test]
    public async Task ListAthleteActivitiesAsync_PageBelowOne_ClampsToOne()
    {
        var handler = new RecordingHandler { Respond = _ => JsonOk("[]") };
        var client = MakeClient(handler);

        await client.ListAthleteActivitiesAsync("at", page: 0, perPage: 5);

        Assert.That(handler.Requests[0].RequestUri!.Query, Does.Contain("page=1"));
    }

    [Test]
    public async Task ListAthleteActivitiesAsync_NullResponseBody_ReturnsEmptyList()
    {
        var handler = new RecordingHandler { Respond = _ => JsonOk("null") };
        var client = MakeClient(handler);

        var result = await client.ListAthleteActivitiesAsync("at");

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Is.Empty);
    }

    [Test]
    public async Task ListAthleteActivitiesAsync_500_ReturnsFailure()
    {
        var handler = new RecordingHandler
        {
            Respond = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("oops"),
                ReasonPhrase = "Internal Server Error"
            }
        };
        var client = MakeClient(handler);

        var result = await client.ListAthleteActivitiesAsync("at");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("500"));
    }

    [Test]
    public async Task ListAthleteActivitiesAsync_NetworkException_ReturnsFailure()
    {
        var handler = new ThrowingHandler(new HttpRequestException("nope"));
        var http = new HttpClient(handler);
        var options = Microsoft.Extensions.Options.Options.Create(new StravaOptions());
        var logger = Substitute.For<ILogger<StravaApiClient>>();
        var client = new StravaApiClient(http, options, logger);

        var result = await client.ListAthleteActivitiesAsync("at");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("nope"));
    }

    [Test]
    public async Task GetActivityAsync_NetworkException_ReturnsFailure()
    {
        var handler = new ThrowingHandler(new HttpRequestException("timeout"));
        var http = new HttpClient(handler);
        var options = Microsoft.Extensions.Options.Options.Create(new StravaOptions());
        var logger = Substitute.For<ILogger<StravaApiClient>>();
        var client = new StravaApiClient(http, options, logger);

        var result = await client.GetActivityAsync("at", 1);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("timeout"));
    }

    [Test]
    public async Task FailFromHttpAsync_TruncatesVeryLargeBodyInLog()
    {
        // No direct API for the truncation helper — verify a 1 000-byte
        // body is accepted without throwing (covers the > max branch).
        var hugeBody = new string('x', 1000);
        var handler = new RecordingHandler
        {
            Respond = _ => new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(hugeBody),
                ReasonPhrase = "Bad Request"
            }
        };
        var client = MakeClient(handler);

        var result = await client.GetActivityAsync("at", 1);

        Assert.That(result.IsSuccess, Is.False);
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        private readonly Exception _toThrow;
        public ThrowingHandler(Exception toThrow) { _toThrow = toThrow; }
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => throw _toThrow;
    }
}
