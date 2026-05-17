using System.Net;
using System.Net.Http;
using System.Text;

namespace MyHomePage.Tests.Services;

/// <summary>
/// Tests for the small media-metadata extractors and the AI-disabled
/// short-circuit on <see cref="ClaudeAssistantService"/>. Reading real
/// EXIF / QuickTime bytes is out of scope here — we lock down the pure
/// helpers and the early-return paths so a regression there fails fast.
/// </summary>
[TestFixture]
public sealed class ExtractorAndAiTests
{
    // ── GpsLocationExtractor ──────────────────────────────────────────────

    [Test]
    public void GpsExtractor_FileDoesNotExist_ReturnsNull()
    {
        var extractor = new GpsLocationExtractor(
            Substitute.For<ILogger<GpsLocationExtractor>>());

        Assert.That(extractor.TryExtract(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".jpg")), Is.Null);
    }

    [Test]
    public void GpsExtractor_UnreadableBytes_ReturnsNullWithoutThrowing()
    {
        var tempPath = Path.GetTempFileName();
        try
        {
            // Write a few non-image bytes so MetadataExtractor throws.
            File.WriteAllBytes(tempPath, new byte[] { 0x00, 0x01, 0x02 });
            var extractor = new GpsLocationExtractor(
                Substitute.For<ILogger<GpsLocationExtractor>>());

            Assert.That(extractor.TryExtract(tempPath), Is.Null);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [TestCase("+50.0614+019.9366/", 50.0614, 19.9366)]
    [TestCase("-33.8688+151.2093/", -33.8688, 151.2093)]
    [TestCase("+48.8584-002.2945/", 48.8584, -2.2945)]
    public void TryParseIso6709_ValidString_ReturnsCoordinates(
        string raw, double expectedLat, double expectedLon)
    {
        var parsed = GpsLocationExtractor.TryParseIso6709(raw, out var coords);

        Assert.That(parsed, Is.True);
        Assert.That(coords.Latitude, Is.EqualTo(expectedLat).Within(0.0001));
        Assert.That(coords.Longitude, Is.EqualTo(expectedLon).Within(0.0001));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("not a coord")]
    [TestCase("50.0614 19.9366")] // no leading sign
    public void TryParseIso6709_InvalidString_ReturnsFalse(string? raw)
    {
        var parsed = GpsLocationExtractor.TryParseIso6709(raw, out var coords);

        Assert.That(parsed, Is.False);
        Assert.That(coords, Is.EqualTo(default(GeoCoordinates)));
    }

    // ── MetadataDateTakenExtractor ────────────────────────────────────────

    [Test]
    public void DateExtractor_FileDoesNotExist_ReturnsNull()
    {
        var extractor = new MetadataDateTakenExtractor(
            Substitute.For<ILogger<MetadataDateTakenExtractor>>());

        Assert.That(extractor.TryExtract(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mp4")), Is.Null);
    }

    [Test]
    public void DateExtractor_UnreadableBytes_ReturnsNullWithoutThrowing()
    {
        var tempPath = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(tempPath, new byte[] { 0x00, 0x01, 0x02 });
            var extractor = new MetadataDateTakenExtractor(
                Substitute.For<ILogger<MetadataDateTakenExtractor>>());

            Assert.That(extractor.TryExtract(tempPath), Is.Null);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    // ── ClaudeAssistantService disabled path ──────────────────────────────

    [Test]
    public async Task ClaudeAssistant_NoApiKey_IsDisabledAndShortCircuitsCalls()
    {
        var originalKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", null);
        try
        {
            var http = new HttpClient(new NoCallsHandler());
            var logger = Substitute.For<ILogger<ClaudeAssistantService>>();
            var service = new ClaudeAssistantService(http, logger);

            Assert.That(service.IsEnabled, Is.False);

            var suggestion = await service.SuggestForUploadAsync(
                VideoCategories.Running, "trail");
            Assert.That(suggestion, Is.Null);

            var location = await service.ExtractLocationAsync(
                "Avatar Kraków - push day", "Pushed hard.", "WeightTraining");
            Assert.That(location, Is.Null);

            var coach = await service.GenerateCoachReportAsync("context block");
            Assert.That(coach, Is.Null);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", originalKey);
        }
    }

    [Test]
    public void ClaudeAssistant_KeySet_IsEnabledTrue()
    {
        var originalKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", "sk-ant-test");
        try
        {
            var http = new HttpClient(new NoCallsHandler());
            var logger = Substitute.For<ILogger<ClaudeAssistantService>>();
            var service = new ClaudeAssistantService(http, logger);

            Assert.That(service.IsEnabled, Is.True);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", originalKey);
        }
    }

    [Test]
    public async Task ClaudeAssistant_EmptyCategoryAndKeywords_ReturnsNull()
    {
        var originalKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", "sk-ant-test");
        try
        {
            var http = new HttpClient(new NoCallsHandler());
            var logger = Substitute.For<ILogger<ClaudeAssistantService>>();
            var service = new ClaudeAssistantService(http, logger);

            var suggestion = await service.SuggestForUploadAsync(
                category: "", keywords: "   ");

            Assert.That(suggestion, Is.Null);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", originalKey);
        }
    }

    /// <summary>
    /// Handler that fails the test if any HTTP call is attempted —
    /// disabled / short-circuit paths must never reach the wire.
    /// </summary>
    private sealed class NoCallsHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                $"Unexpected HTTP call to {request.RequestUri}");
    }
}
