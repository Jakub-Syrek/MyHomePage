namespace MyHomePage.Services;

/// <summary>
/// Writes the recommended OWASP security headers on every non-scraper
/// response. Skipped for scraper traffic so Facebook / WhatsApp / Slack
/// previews don't get blocked by a strict CSP — those clients already
/// see a hardened <c>/og/{id}</c> body which the
/// <see cref="ScraperRewriteMiddleware"/> rewrites.
///
/// Set in front of the rest of the pipeline so headers ride on every
/// response, including error pages.
/// </summary>
public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    /// <summary>Content-Security-Policy applied to interactive responses.</summary>
    /// <remarks>
    /// Blazor Server requires inline + WASM-style scripts to bootstrap the
    /// SignalR runtime; <c>'unsafe-inline'</c> on script-src is unfortunately
    /// the standard recommendation today. Style-src includes <c>'unsafe-inline'</c>
    /// because Razor components emit scoped inline style attributes for
    /// dynamic values. <c>img-src</c> is loose so user-uploaded gallery
    /// items render; <c>connect-src</c> covers the SignalR back-channel
    /// and Strava / Anthropic API calls. <c>frame-ancestors 'none'</c>
    /// is the modern equivalent of <c>X-Frame-Options: DENY</c>.
    /// </remarks>
    private const string ContentSecurityPolicy =
        "default-src 'self'; " +
        // unpkg.com is whitelisted for the Leaflet bundle used on the
        // /map page — both the script and its companion stylesheet.
        // Without this CSP blocks the CDN load and the map renders as
        // an empty container.
        "script-src 'self' 'unsafe-inline' 'unsafe-eval' https://unpkg.com; " +
        "style-src 'self' 'unsafe-inline' https://unpkg.com; " +
        "img-src 'self' data: blob: https:; " +
        "media-src 'self' blob:; " +
        "font-src 'self' data:; " +
        "connect-src 'self' https://api.anthropic.com https://www.strava.com https://nominatim.openstreetmap.org wss://*; " +
        "form-action 'self' https://www.strava.com; " +
        "frame-ancestors 'none'; " +
        "base-uri 'self'; " +
        "object-src 'none'";

    /// <summary>
    /// Permissions-Policy lockdown — the gallery doesn't use any of the
    /// listed sensors / hardware APIs, so every feature is denied by
    /// default. Reduces fingerprinting surface and prevents third-party
    /// scripts (or accidentally-injected ones) from poking at hardware.
    /// </summary>
    private const string PermissionsPolicy =
        "accelerometer=(), " +
        "ambient-light-sensor=(), " +
        "autoplay=(self), " +
        "battery=(), " +
        "camera=(), " +
        "display-capture=(), " +
        "encrypted-media=(), " +
        "fullscreen=(self), " +
        "geolocation=(), " +
        "gyroscope=(), " +
        "keyboard-map=(), " +
        "magnetometer=(), " +
        "microphone=(), " +
        "midi=(), " +
        "payment=(), " +
        "picture-in-picture=(self), " +
        "publickey-credentials-get=(self), " +
        "screen-wake-lock=(), " +
        "sync-xhr=(), " +
        "usb=(), " +
        "xr-spatial-tracking=()";

    /// <summary>
    /// Creates the middleware with the next pipeline delegate.
    /// </summary>
    /// <param name="next">Next request delegate in the pipeline.</param>
    public SecurityHeadersMiddleware(RequestDelegate next) => _next = next;

    /// <summary>
    /// Attaches every header just before the response starts so they're
    /// emitted regardless of which downstream handler produces the body.
    /// </summary>
    /// <param name="context">Current HTTP context.</param>
    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            // Skip the hardening on scraper traffic so social previews
            // aren't blocked by CSP / frame-ancestors. The ScraperRewrite
            // middleware already routed them through /og/{id}.
            if (context.Items.TryGetValue(
                    ScraperRewriteMiddleware.ContextKey, out var v)
                && v is true)
            {
                return Task.CompletedTask;
            }

            var headers = context.Response.Headers;
            if (!headers.ContainsKey("Content-Security-Policy"))
                headers["Content-Security-Policy"] = ContentSecurityPolicy;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Permissions-Policy"] = PermissionsPolicy;
            // Cross-origin isolation — denies the page from being embedded
            // or accessed cross-origin without explicit opt-in.
            headers["Cross-Origin-Opener-Policy"] = "same-origin";
            headers["Cross-Origin-Resource-Policy"] = "same-origin";
            // Old IE / legacy proxies; modern browsers ignore it but it
            // does no harm.
            headers["X-Permitted-Cross-Domain-Policies"] = "none";
            return Task.CompletedTask;
        });
        return _next(context);
    }
}

/// <summary>
/// Extension that registers <see cref="SecurityHeadersMiddleware"/> in
/// the pipeline with a single fluent call.
/// </summary>
public static class SecurityHeadersMiddlewareExtensions
{
    /// <summary>
    /// Adds <see cref="SecurityHeadersMiddleware"/> to the pipeline.
    /// </summary>
    /// <param name="app">Application builder to extend.</param>
    /// <returns>The same builder for fluent chaining.</returns>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app)
        => app.UseMiddleware<SecurityHeadersMiddleware>();
}
