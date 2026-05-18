using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace MyHomePage.Tests.Services;

/// <summary>
/// Tests for <see cref="SecurityHeadersMiddleware"/>. Verifies every
/// header lands on a non-scraper response and that scraper traffic is
/// left alone so social previews still render.
/// </summary>
[TestFixture]
public sealed class SecurityHeadersMiddlewareTests
{
    [Test]
    public async Task InvokeAsync_NonScraperRequest_AddsAllSecurityHeaders()
    {
        var feature = new CapturingResponseFeature();
        var ctx = MakeContext(feature, isScraper: false);
        var middleware = new SecurityHeadersMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(ctx);
        await feature.FireOnStartingAsync();

        Assert.That(ctx.Response.Headers["Content-Security-Policy"].ToString(),
            Does.Contain("default-src 'self'"));
        Assert.That(ctx.Response.Headers["X-Content-Type-Options"].ToString(),
            Is.EqualTo("nosniff"));
        Assert.That(ctx.Response.Headers["X-Frame-Options"].ToString(),
            Is.EqualTo("DENY"));
        Assert.That(ctx.Response.Headers["Referrer-Policy"].ToString(),
            Is.EqualTo("strict-origin-when-cross-origin"));
        Assert.That(ctx.Response.Headers["Permissions-Policy"].ToString(),
            Does.Contain("camera=()"));
        Assert.That(ctx.Response.Headers["Cross-Origin-Opener-Policy"].ToString(),
            Is.EqualTo("same-origin"));
        Assert.That(ctx.Response.Headers["Cross-Origin-Resource-Policy"].ToString(),
            Is.EqualTo("same-origin"));
    }

    [Test]
    public async Task InvokeAsync_ScraperFlagged_DoesNotWriteSecurityHeaders()
    {
        var feature = new CapturingResponseFeature();
        var ctx = MakeContext(feature, isScraper: true);
        var middleware = new SecurityHeadersMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(ctx);
        await feature.FireOnStartingAsync();

        Assert.That(ctx.Response.Headers.ContainsKey("Content-Security-Policy"),
            Is.False);
        Assert.That(ctx.Response.Headers.ContainsKey("X-Frame-Options"),
            Is.False);
    }

    [Test]
    public async Task InvokeAsync_CspAlreadySetUpstream_DoesNotOverwrite()
    {
        var feature = new CapturingResponseFeature();
        var ctx = MakeContext(feature, isScraper: false);
        // Pretend a more specific handler already wrote a stricter CSP.
        ctx.Response.Headers["Content-Security-Policy"] = "default-src 'none'";
        var middleware = new SecurityHeadersMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(ctx);
        await feature.FireOnStartingAsync();

        Assert.That(ctx.Response.Headers["Content-Security-Policy"].ToString(),
            Is.EqualTo("default-src 'none'"));
    }

    [Test]
    public async Task InvokeAsync_AlwaysCallsNext()
    {
        var nextCalled = false;
        var feature = new CapturingResponseFeature();
        var ctx = MakeContext(feature, isScraper: false);
        var middleware = new SecurityHeadersMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(ctx);

        Assert.That(nextCalled, Is.True);
    }

    private static DefaultHttpContext MakeContext(
        CapturingResponseFeature feature, bool isScraper)
    {
        var ctx = new DefaultHttpContext();
        ctx.Features.Set<IHttpResponseFeature>(feature);
        if (isScraper)
            ctx.Items[ScraperRewriteMiddleware.ContextKey] = true;
        return ctx;
    }

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
