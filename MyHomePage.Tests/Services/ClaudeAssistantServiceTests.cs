using System.Net;
using System.Net.Http;
using System.Text;

namespace MyHomePage.Tests.Services;

/// <summary>
/// HTTP-level tests for <see cref="ClaudeAssistantService"/>. The
/// Anthropic API is replaced by a fake <see cref="HttpMessageHandler"/>
/// so each tool-use entry point (<c>SuggestForUpload</c>,
/// <c>ExtractLocation</c>, <c>GenerateCoachReport</c>) can be exercised
/// against a hand-rolled response without touching the network.
/// </summary>
[TestFixture]
public sealed class ClaudeAssistantServiceTests
{
    private string? _origApiKey;

    [SetUp]
    public void SetApiKey()
    {
        _origApiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", "sk-ant-test");
    }

    [TearDown]
    public void RestoreApiKey()
    {
        Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", _origApiKey);
    }

    // ── SuggestForUploadAsync ─────────────────────────────────────────────

    [Test]
    public async Task SuggestForUploadAsync_ParsesToolUseInputIntoSuggestion()
    {
        const string body = """
        {
          "content": [
            {
              "type": "tool_use",
              "name": "suggest_upload_metadata",
              "input": {
                "title": "Granite Spires",
                "description": "Two pitches of clean granite.",
                "location": "Tatra Mountains, Poland",
                "latitude": 49.27,
                "longitude": 20.0
              }
            }
          ]
        }
        """;
        var service = MakeService(JsonOk(body), out _);

        var suggestion = await service.SuggestForUploadAsync(
            VideoCategories.WspinaczkaSkalowa, "granite alpine");

        Assert.That(suggestion, Is.Not.Null);
        Assert.That(suggestion!.Title, Is.EqualTo("Granite Spires"));
        Assert.That(suggestion.Location, Is.EqualTo("Tatra Mountains, Poland"));
        Assert.That(suggestion.Latitude, Is.EqualTo(49.27));
    }

    [Test]
    public async Task SuggestForUploadAsync_NoToolUseBlock_ReturnsNull()
    {
        const string body = """
        { "content": [ { "type": "text", "text": "plain reply" } ] }
        """;
        var service = MakeService(JsonOk(body), out _);

        var suggestion = await service.SuggestForUploadAsync("Running", "trail");

        Assert.That(suggestion, Is.Null);
    }

    [Test]
    public async Task SuggestForUploadAsync_ApiReturns429_ReturnsNull()
    {
        var service = MakeService(
            new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent("rate limited")
            }, out _);

        var suggestion = await service.SuggestForUploadAsync("Running", "trail");

        Assert.That(suggestion, Is.Null);
    }

    [Test]
    public async Task SuggestForUploadAsync_NetworkException_ReturnsNull()
    {
        var service = MakeServiceThatThrows(new HttpRequestException("conn refused"));

        var suggestion = await service.SuggestForUploadAsync("Running", "trail");

        Assert.That(suggestion, Is.Null);
    }

    [Test]
    public async Task SuggestForUploadAsync_MalformedJson_ReturnsNull()
    {
        var service = MakeService(JsonOk("not json {"), out _);

        var suggestion = await service.SuggestForUploadAsync("Running", "trail");

        Assert.That(suggestion, Is.Null);
    }

    [Test]
    public async Task SuggestForUploadAsync_SendsApiKeyAndVersionHeaders()
    {
        var service = MakeService(JsonOk("""
        { "content": [ { "type": "tool_use", "input": { "title": "t", "description": "d" } } ] }
        """), out var handler);

        await service.SuggestForUploadAsync("Running", "trail");

        Assert.That(handler.Requests, Has.Count.EqualTo(1));
        Assert.That(handler.Requests[0].Headers.GetValues("x-api-key").First(),
            Is.EqualTo("sk-ant-test"));
        Assert.That(handler.Requests[0].Headers.GetValues("anthropic-version").First(),
            Is.EqualTo("2023-06-01"));
    }

    [Test]
    public async Task SuggestForUploadAsync_OnlyKeywords_SendsKeywordsOnlyMessage()
    {
        var service = MakeService(JsonOk("""
        { "content": [ { "type": "tool_use", "input": { "title": "t", "description": "d" } } ] }
        """), out var handler);

        await service.SuggestForUploadAsync(category: "", keywords: "trail");

        Assert.That(handler.RequestBodies[0], Does.Contain("Keywords: trail"));
        Assert.That(handler.RequestBodies[0], Does.Not.Contain("Category:"));
    }

    // ── ExtractLocationAsync ──────────────────────────────────────────────

    [Test]
    public async Task ExtractLocationAsync_HappyPath_ReturnsLocationString()
    {
        var service = MakeService(JsonOk("""
        { "content": [ { "type": "tool_use", "input": { "location": "Avatar Kraków" } } ] }
        """), out _);

        var location = await service.ExtractLocationAsync(
            "Avatar Kraków - push day", "Tough session.", "WeightTraining");

        Assert.That(location, Is.EqualTo("Avatar Kraków"));
    }

    [Test]
    public async Task ExtractLocationAsync_LocationFieldNull_ReturnsNull()
    {
        var service = MakeService(JsonOk("""
        { "content": [ { "type": "tool_use", "input": { "location": null } } ] }
        """), out _);

        var location = await service.ExtractLocationAsync(
            "Morning Run", "", "Run");

        Assert.That(location, Is.Null);
    }

    [Test]
    public async Task ExtractLocationAsync_Whitespace_ReturnsNull()
    {
        var service = MakeService(JsonOk("""
        { "content": [ { "type": "tool_use", "input": { "location": "   " } } ] }
        """), out _);

        var location = await service.ExtractLocationAsync(
            "Morning Run", "", "Run");

        Assert.That(location, Is.Null);
    }

    [Test]
    public async Task ExtractLocationAsync_EmptyInputs_ReturnsNullWithoutHttpCall()
    {
        var service = MakeService(JsonOk("never used"), out var handler);

        var location = await service.ExtractLocationAsync("   ", "   ", "Run");

        Assert.That(location, Is.Null);
        Assert.That(handler.Requests, Is.Empty);
    }

    [Test]
    public async Task ExtractLocationAsync_ApiError_ReturnsNull()
    {
        var service = MakeService(
            new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("err")
            }, out _);

        var location = await service.ExtractLocationAsync(
            "Title", "Desc", "Run");

        Assert.That(location, Is.Null);
    }

    [Test]
    public async Task ExtractLocationAsync_NetworkException_ReturnsNull()
    {
        var service = MakeServiceThatThrows(new HttpRequestException("offline"));

        var location = await service.ExtractLocationAsync("Title", "Desc", "Run");

        Assert.That(location, Is.Null);
    }

    // ── GenerateCoachReportAsync ──────────────────────────────────────────

    [Test]
    public async Task GenerateCoachReportAsync_HappyPath_ParsesAllFields()
    {
        var service = MakeService(JsonOk("""
        {
          "content": [
            {
              "type": "tool_use",
              "input": {
                "headline": "Solid build week.",
                "narrative": "Three runs, solid load.",
                "highlights": ["5k easy at MAF", "long-run pace held"],
                "concerns": ["HR drift on Sunday"],
                "next_week_focus": ["cap Z2", "tempo Tuesday"]
              }
            }
          ]
        }
        """), out _);

        var report = await service.GenerateCoachReportAsync(
            "Week 2026-W20\n=== This week ===\nSessions: 3\n");

        Assert.That(report, Is.Not.Null);
        Assert.That(report!.Headline, Is.EqualTo("Solid build week."));
        Assert.That(report.Highlights, Has.Count.EqualTo(2));
        Assert.That(report.Concerns, Has.Count.EqualTo(1));
        Assert.That(report.NextWeekFocus, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task GenerateCoachReportAsync_EmptyContext_ReturnsNullWithoutCall()
    {
        var service = MakeService(JsonOk("never used"), out var handler);

        var report = await service.GenerateCoachReportAsync("   ");

        Assert.That(report, Is.Null);
        Assert.That(handler.Requests, Is.Empty);
    }

    [Test]
    public async Task GenerateCoachReportAsync_NoToolUseBlock_ReturnsNull()
    {
        var service = MakeService(JsonOk("""
        { "content": [ { "type": "text", "text": "no tool call" } ] }
        """), out _);

        var report = await service.GenerateCoachReportAsync("context");

        Assert.That(report, Is.Null);
    }

    [Test]
    public async Task GenerateCoachReportAsync_NetworkException_ReturnsNull()
    {
        var service = MakeServiceThatThrows(new HttpRequestException("nope"));

        var report = await service.GenerateCoachReportAsync("context");

        Assert.That(report, Is.Null);
    }

    [Test]
    public async Task GenerateCoachReportAsync_ApiError_ReturnsNull()
    {
        var service = MakeService(
            new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("bad")
            }, out _);

        var report = await service.GenerateCoachReportAsync("context");

        Assert.That(report, Is.Null);
    }

    [Test]
    public async Task GenerateCoachReportAsync_NoContentArray_ReturnsNull()
    {
        var service = MakeService(JsonOk("""{ "other": "field" }"""), out _);

        var report = await service.GenerateCoachReportAsync("context");

        Assert.That(report, Is.Null);
    }

    [Test]
    public async Task GenerateCoachReportAsync_MalformedJson_ReturnsNull()
    {
        var service = MakeService(JsonOk("{ broken json"), out _);

        var report = await service.GenerateCoachReportAsync("context");

        Assert.That(report, Is.Null);
    }

    // ── helpers ───────────────────────────────────────────────────────────

    private static HttpResponseMessage JsonOk(string body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    private static ClaudeAssistantService MakeService(
        HttpResponseMessage response, out RecordingHandler handler)
    {
        handler = new RecordingHandler(_ => response);
        var http = new HttpClient(handler);
        var logger = Substitute.For<ILogger<ClaudeAssistantService>>();
        return new ClaudeAssistantService(http, logger);
    }

    private static ClaudeAssistantService MakeServiceThatThrows(Exception toThrow)
    {
        var handler = new ThrowingHandler(toThrow);
        var http = new HttpClient(handler);
        var logger = Substitute.For<ILogger<ClaudeAssistantService>>();
        return new ClaudeAssistantService(http, logger);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
        public List<HttpRequestMessage> Requests { get; } = new();
        public List<string?> RequestBodies { get; } = new();

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            _respond = respond;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string? body = null;
            if (request.Content is not null)
                body = await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(request);
            RequestBodies.Add(body);
            return _respond(request);
        }
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
