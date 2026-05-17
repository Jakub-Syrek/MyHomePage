using System.Net;
using System.Net.Http;
using System.Text;

namespace MyHomePage.Tests.Services;

/// <summary>
/// Tests for <see cref="NominatimReverseGeocoder"/>. The HTTP boundary is
/// stubbed with a fake <see cref="HttpMessageHandler"/> so the JSON
/// composition logic (most-specific tier, country suffix, failure
/// swallowing) can be verified deterministically.
/// </summary>
[TestFixture]
public sealed class NominatimReverseGeocoderTests
{
    [Test]
    public async Task ResolveAsync_NormalPayload_PicksCityAndAppendsCountry()
    {
        var geocoder = MakeGeocoder("""
        {
            "address": {
                "city": "Kraków",
                "state": "Lesser Poland Voivodeship",
                "country": "Poland"
            }
        }
        """);

        var label = await geocoder.ResolveAsync(50.06, 19.94);

        Assert.That(label, Is.EqualTo("Kraków, Poland"));
    }

    [Test]
    public async Task ResolveAsync_SuburbPresent_PrefersSuburbOverCity()
    {
        var geocoder = MakeGeocoder("""
        {
            "address": {
                "suburb": "Krowodrza",
                "city": "Kraków",
                "country": "Poland"
            }
        }
        """);

        var label = await geocoder.ResolveAsync(50.07, 19.93);

        Assert.That(label, Is.EqualTo("Krowodrza, Poland"));
    }

    [Test]
    public async Task ResolveAsync_FallsBackToStateWhenNoCityTier()
    {
        var geocoder = MakeGeocoder("""
        {
            "address": {
                "state": "Bavaria",
                "country": "Germany"
            }
        }
        """);

        var label = await geocoder.ResolveAsync(48.0, 11.5);

        Assert.That(label, Is.EqualTo("Bavaria, Germany"));
    }

    [Test]
    public async Task ResolveAsync_NoCountry_ReturnsBareLabel()
    {
        var geocoder = MakeGeocoder("""
        {
            "address": {
                "city": "Atlantis"
            }
        }
        """);

        var label = await geocoder.ResolveAsync(0, 0);

        Assert.That(label, Is.EqualTo("Atlantis"));
    }

    [Test]
    public async Task ResolveAsync_EmptyAddress_ReturnsNull()
    {
        var geocoder = MakeGeocoder("""
        { "address": {} }
        """);

        var label = await geocoder.ResolveAsync(0, 0);

        Assert.That(label, Is.Null);
    }

    [Test]
    public async Task ResolveAsync_NonSuccessHttpStatus_ReturnsNull()
    {
        var geocoder = MakeGeocoderForStatus(HttpStatusCode.TooManyRequests);

        var label = await geocoder.ResolveAsync(50, 19);

        Assert.That(label, Is.Null);
    }

    [Test]
    public async Task ResolveAsync_NetworkException_ReturnsNullInsteadOfThrowing()
    {
        var handler = new FakeHandler(_ => throw new HttpRequestException("network down"));
        var http = new HttpClient(handler);
        var logger = Substitute.For<ILogger<NominatimReverseGeocoder>>();
        var geocoder = new NominatimReverseGeocoder(http, logger);

        var label = await geocoder.ResolveAsync(50, 19);

        Assert.That(label, Is.Null);
    }

    [Test]
    public async Task ResolveAsync_AddsRequiredUserAgentHeader()
    {
        HttpRequestMessage? captured = null;
        var handler = new FakeHandler(request =>
        {
            captured = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{ "address": {} }""", Encoding.UTF8, "application/json")
            };
        });
        var http = new HttpClient(handler);
        var logger = Substitute.For<ILogger<NominatimReverseGeocoder>>();
        var geocoder = new NominatimReverseGeocoder(http, logger);

        await geocoder.ResolveAsync(50, 19);

        Assert.That(captured, Is.Not.Null);
        Assert.That(http.DefaultRequestHeaders.UserAgent.Count, Is.GreaterThan(0));
    }

    private static NominatimReverseGeocoder MakeGeocoder(string responseBody)
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
        });
        var http = new HttpClient(handler);
        var logger = Substitute.For<ILogger<NominatimReverseGeocoder>>();
        return new NominatimReverseGeocoder(http, logger);
    }

    private static NominatimReverseGeocoder MakeGeocoderForStatus(HttpStatusCode status)
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(status));
        var http = new HttpClient(handler);
        var logger = Substitute.For<ILogger<NominatimReverseGeocoder>>();
        return new NominatimReverseGeocoder(http, logger);
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            _respond = respond;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_respond(request));
        }
    }
}
