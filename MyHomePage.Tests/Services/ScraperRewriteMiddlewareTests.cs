using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace MyHomePage.Tests.Services;

/// <summary>
/// Tests for <see cref="ScraperRewriteMiddleware"/>. The middleware is
/// exercised against a real <see cref="DefaultHttpContext"/> and a
/// captured next-delegate so the path-rewrite logic and the
/// response-shape callbacks can both be verified end-to-end.
/// </summary>
[TestFixture]
public sealed class ScraperRewriteMiddlewareTests
{
    [TestCase("facebookexternalhit/1.1", true)]
    [TestCase("WhatsApp/2.20.1", true)]
    [TestCase("Twitterbot/1.0", true)]
    [TestCase("LinkedInBot/1.0 (compatible; Mozilla/5.0; +https://...)", true)]
    [TestCase("Slackbot 1.0 (+https://slack.com)", true)]
    [TestCase("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120.0", false)]
    [TestCase("curl/7.81.0", false)]
    [TestCase("", false)]
    [TestCase(null, false)]
    public void IsScraper_DetectsKnownScraperUserAgents(string? userAgent, bool expected)
    {
        Assert.That(ScraperRewriteMiddleware.IsScraper(userAgent), Is.EqualTo(expected));
    }

    [Test]
    public async Task InvokeAsync_BrowserUserAgent_DoesNotRewrite()
    {
        var ctx = MakeContext(userAgent: "Mozilla/5.0", path: "/item/42");
        var (middleware, nextCalled) = MakeMiddleware();

        await middleware.InvokeAsync(ctx);

        Assert.That(nextCalled.Value, Is.True);
        Assert.That(ctx.Items[ScraperRewriteMiddleware.ContextKey], Is.Null);
        Assert.That(ctx.Request.Path.Value, Is.EqualTo("/item/42"));
    }

    [Test]
    public async Task InvokeAsync_FacebookCrawlingItemRoute_RewritesToOg()
    {
        var ctx = MakeContext(
            userAgent: "facebookexternalhit/1.1",
            path: "/item/123");
        var (middleware, _) = MakeMiddleware();

        await middleware.InvokeAsync(ctx);

        Assert.That(ctx.Items[ScraperRewriteMiddleware.ContextKey], Is.True);
        Assert.That(ctx.Request.Path.Value, Is.EqualTo("/og/123"));
    }

    [Test]
    public async Task InvokeAsync_ScraperOnNonItemRoute_DoesNotRewritePath()
    {
        var ctx = MakeContext(
            userAgent: "Twitterbot/1.0",
            path: "/gory");
        var (middleware, _) = MakeMiddleware();

        await middleware.InvokeAsync(ctx);

        Assert.That(ctx.Items[ScraperRewriteMiddleware.ContextKey], Is.True);
        Assert.That(ctx.Request.Path.Value, Is.EqualTo("/gory"));
    }

    [Test]
    public async Task InvokeAsync_Scraper_ForcesIdentityAcceptEncoding()
    {
        var ctx = MakeContext(userAgent: "WhatsApp/2.20.1", path: "/");
        ctx.Request.Headers["Accept-Encoding"] = "gzip, br";
        var (middleware, _) = MakeMiddleware();

        await middleware.InvokeAsync(ctx);

        Assert.That(ctx.Request.Headers["Accept-Encoding"].ToString(), Is.EqualTo("identity"));
    }

    [Test]
    public async Task InvokeAsync_ScraperOnPost_LeavesPathAlone()
    {
        var ctx = MakeContext(userAgent: "Slackbot 1.0", path: "/item/42", method: "POST");
        var (middleware, _) = MakeMiddleware();

        await middleware.InvokeAsync(ctx);

        Assert.That(ctx.Request.Path.Value, Is.EqualTo("/item/42"));
        Assert.That(ctx.Items[ScraperRewriteMiddleware.ContextKey], Is.True);
    }

    [Test]
    public async Task InvokeAsync_ItemRouteWithTrailingSlash_StillRewrites()
    {
        var ctx = MakeContext(userAgent: "DiscordBot/1.0", path: "/item/777/");
        var (middleware, _) = MakeMiddleware();

        await middleware.InvokeAsync(ctx);

        Assert.That(ctx.Request.Path.Value, Is.EqualTo("/og/777"));
    }

    [Test]
    public async Task InvokeAsync_ItemRouteCaseInsensitive_StillRewrites()
    {
        var ctx = MakeContext(userAgent: "facebookcatalog/1.0", path: "/Item/55");
        var (middleware, _) = MakeMiddleware();

        await middleware.InvokeAsync(ctx);

        Assert.That(ctx.Request.Path.Value, Is.EqualTo("/og/55"));
    }

    [Test]
    public async Task InvokeAsync_Scraper_ResponseStartCallbackAddsRobotsAndCache()
    {
        var feature = new CapturingResponseFeature();
        var ctx = MakeContext(userAgent: "Googlebot/2.1", path: "/", responseFeature: feature);
        var (middleware, _) = MakeMiddleware();

        await middleware.InvokeAsync(ctx);
        await feature.FireOnStartingAsync();

        Assert.That(ctx.Response.Headers["X-Robots-Tag"].ToString(), Is.EqualTo("all"));
        Assert.That(ctx.Response.Headers["Cache-Control"].ToString(),
            Does.Contain("public, max-age=600"));
    }

    [Test]
    public async Task InvokeAsync_Scraper_OnStartingReplacesNoStoreCache()
    {
        var feature = new CapturingResponseFeature();
        var ctx = MakeContext(userAgent: "Embedly", path: "/", responseFeature: feature);
        ctx.Response.Headers["Cache-Control"] = "no-store";
        var (middleware, _) = MakeMiddleware();

        await middleware.InvokeAsync(ctx);
        await feature.FireOnStartingAsync();

        Assert.That(ctx.Response.Headers["Cache-Control"].ToString(),
            Does.Contain("public, max-age=600"));
    }

    [Test]
    public async Task InvokeAsync_Scraper_OnStartingRemovesHsts()
    {
        var feature = new CapturingResponseFeature();
        var ctx = MakeContext(userAgent: "Bingbot/2.0", path: "/", responseFeature: feature);
        ctx.Response.Headers["Strict-Transport-Security"] = "max-age=31536000";
        var (middleware, _) = MakeMiddleware();

        await middleware.InvokeAsync(ctx);
        await feature.FireOnStartingAsync();

        Assert.That(ctx.Response.Headers.ContainsKey("Strict-Transport-Security"),
            Is.False);
    }

    [Test]
    public async Task InvokeAsync_Scraper_OnStartingDoesNotOverridePageSpecificCacheControl()
    {
        var feature = new CapturingResponseFeature();
        var ctx = MakeContext(userAgent: "Pinterestbot/1.0", path: "/", responseFeature: feature);
        ctx.Response.Headers["Cache-Control"] = "public, max-age=86400";
        var (middleware, _) = MakeMiddleware();

        await middleware.InvokeAsync(ctx);
        await feature.FireOnStartingAsync();

        // Page already set a fine Cache-Control — the middleware should
        // not clobber it with the default.
        Assert.That(ctx.Response.Headers["Cache-Control"].ToString(),
            Does.Contain("max-age=86400"));
    }

    // UseScraperRewrite extension is exercised indirectly by registering
    // the middleware in DI inside the production Program.cs — we don't
    // re-test the framework's UseMiddleware<T>() plumbing here.

    // ── helpers ───────────────────────────────────────────────────────────

    private static DefaultHttpContext MakeContext(
        string? userAgent, string path, string method = "GET",
        CapturingResponseFeature? responseFeature = null)
    {
        var ctx = new DefaultHttpContext();
        if (responseFeature is not null)
            ctx.Features.Set<IHttpResponseFeature>(responseFeature);
        if (userAgent is not null) ctx.Request.Headers["User-Agent"] = userAgent;
        ctx.Request.Path = path;
        ctx.Request.Method = method;
        return ctx;
    }

    private static (ScraperRewriteMiddleware Middleware, BoxedBool NextCalled) MakeMiddleware()
    {
        var nextCalled = new BoxedBool();
        var next = new RequestDelegate(_ =>
        {
            nextCalled.Value = true;
            return Task.CompletedTask;
        });
        var logger = Substitute.For<ILogger<ScraperRewriteMiddleware>>();
        return (new ScraperRewriteMiddleware(next, logger), nextCalled);
    }

    private sealed class BoxedBool { public bool Value { get; set; } }

    /// <summary>
    /// Captures every OnStarting callback the middleware registers so
    /// tests can fire them on demand and verify the resulting headers.
    /// DefaultHttpContext's stub feature swallows them silently, which is
    /// useless for unit-testing header-mutation logic.
    /// </summary>
    private sealed class CapturingResponseFeature : IHttpResponseFeature
    {
        private readonly List<(Func<object, Task> Callback, object State)> _starting = new();

        public int StatusCode { get; set; } = 200;
        public string? ReasonPhrase { get; set; }
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public Stream Body { get; set; } = Stream.Null;
        public bool HasStarted { get; private set; }

        public void OnStarting(Func<object, Task> callback, object state) =>
            _starting.Add((callback, state));

        public void OnCompleted(Func<object, Task> callback, object state) { }

        public async Task FireOnStartingAsync()
        {
            HasStarted = true;
            foreach (var (cb, state) in _starting)
                await cb(state);
        }
    }
}
