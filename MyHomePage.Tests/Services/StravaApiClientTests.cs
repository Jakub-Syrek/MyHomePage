using System.Web;

namespace MyHomePage.Tests.Services;

/// <summary>
/// Tests for the pure / static helpers on <see cref="StravaApiClient"/>. The
/// HTTP-using methods are exercised end-to-end through
/// <see cref="StravaSyncServiceTests"/> with a mocked <c>IStravaApiClient</c>;
/// here we lock down the static authorize-URL builder which has no other
/// coverage.
/// </summary>
[TestFixture]
public sealed class StravaApiClientTests
{
    [Test]
    public void BuildAuthorizeUrl_IncludesAllOauthQueryParameters()
    {
        var options = new StravaOptions
        {
            ClientId = "12345",
            ClientSecret = "secret",
            RedirectUri = "https://example.com/auth/strava/callback",
            Scope = "read,activity:read_all"
        };

        var url = StravaApiClient.BuildAuthorizeUrl(options, state: "anti-forgery-abc");

        Assert.That(url, Does.StartWith("https://www.strava.com/oauth/authorize?"));

        var query = HttpUtility.ParseQueryString(new Uri(url).Query);
        Assert.That(query["client_id"], Is.EqualTo("12345"));
        Assert.That(query["redirect_uri"], Is.EqualTo("https://example.com/auth/strava/callback"));
        Assert.That(query["response_type"], Is.EqualTo("code"));
        Assert.That(query["scope"], Is.EqualTo("read,activity:read_all"));
        Assert.That(query["approval_prompt"], Is.EqualTo("auto"));
        Assert.That(query["state"], Is.EqualTo("anti-forgery-abc"));
    }

    [Test]
    public void BuildAuthorizeUrl_EscapesValuesThatNeedEncoding()
    {
        var options = new StravaOptions
        {
            ClientId = "1",
            RedirectUri = "https://example.com/auth/strava/callback?env=prod",
            Scope = "read,activity:read_all"
        };

        var url = StravaApiClient.BuildAuthorizeUrl(options, state: "needs space & symbols");

        // The raw URL must not leak the unescaped space or ampersand from
        // the state value — they need percent-encoding.
        Assert.That(url, Does.Not.Contain("needs space"));

        var query = HttpUtility.ParseQueryString(new Uri(url).Query);
        Assert.That(query["state"], Is.EqualTo("needs space & symbols"));
        Assert.That(query["redirect_uri"], Is.EqualTo("https://example.com/auth/strava/callback?env=prod"));
    }

    [Test]
    public void BuildAuthorizeUrl_NullOptions_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            StravaApiClient.BuildAuthorizeUrl(null!, state: "s"));
    }
}
