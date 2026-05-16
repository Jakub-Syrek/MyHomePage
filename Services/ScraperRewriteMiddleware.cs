using System.Text.RegularExpressions;

namespace MyHomePage.Services;

/// <summary>
/// Bullet-proof handling of social-media scrapers (Facebook, WhatsApp,
/// Twitter, LinkedIn, Discord, Slack, etc.).
///
/// Goals:
///   • Never treat a known scraper as suspicious — bypass rate-limiters,
///     auth requirements and antiforgery for their requests.
///   • For GET /item/{id} → internally rewrite to /og/{id} so they see a
///     server-rendered Open Graph HTML document (Blazor's HeadContent is
///     invisible to non-JS clients).
///   • Strip HSTS / compression / restrictive cache headers from the
///     response so the scraper can store the page comfortably.
///
/// Other middlewares can opt-in by checking
///   <c>context.Items["IsScraper"]</c>.
/// </summary>
public sealed class ScraperRewriteMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ScraperRewriteMiddleware> _logger;

    public const string ContextKey = "IsScraper";

    // Match /item/{id} (digits only). Optional trailing slash / query.
    private static readonly Regex ItemRoute = new(
        @"^/item/(\d+)/?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Known scraper user-agents. Lower-case substring match.
    // Facebook / WhatsApp explicitly listed at the top so they're impossible
    // to overlook — the rest follow alphabetically-ish.
    private static readonly string[] ScraperSubstrings =
    {
        // === Meta family ===========
        "facebookexternalhit",
        "facebookcatalog",
        "meta-externalagent",
        "facebot",
        "whatsapp",
        "instagram",            // some IG previews use Instagram in UA
        // === Other socials =========
        "twitterbot",
        "linkedinbot",
        "slackbot",
        "slackbot-linkexpanding",
        "discordbot",
        "telegrambot",
        "skypeuripreview",
        "pinterestbot",
        "redditbot",
        "vkshare",
        // === Apple / messaging =====
        "applebot",
        // === Aggregators ===========
        "embedly",
        "iframely",
        // === Search engines ========
        "google-inspectiontool",
        "googlebot",
        "bingbot",
        "duckduckbot",
        "yahoo!slurp",
        "yandexbot"
    };

    public ScraperRewriteMiddleware(RequestDelegate next, ILogger<ScraperRewriteMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var ua = context.Request.Headers.UserAgent.ToString();
        var isScraper = IsScraper(ua);

        if (isScraper)
        {
            // Tag the request so any later middleware can short-circuit
            // (e.g. skip auth, skip rate limit) if it knows about this flag.
            context.Items[ContextKey] = true;

            // Disable response compression for scrapers. The Content-Encoding
            // header is sometimes stripped by the upstream Railway edge proxy,
            // leaving the client with a gzipped body it can't decode. By
            // overriding Accept-Encoding to "identity" we force ASP.NET's
            // ResponseCompression middleware to send plain bytes.
            context.Request.Headers["Accept-Encoding"] = "identity";

            // GET /item/{id} → rewrite to server-rendered /og/{id}
            var path = context.Request.Path.Value;
            if (path is not null && context.Request.Method == "GET")
            {
                var match = ItemRoute.Match(path);
                if (match.Success)
                {
                    var id = match.Groups[1].Value;
                    _logger.LogInformation(
                        "Scraper detected ({UA}) — rewriting {From} → /og/{Id}",
                        ua, path, id);
                    context.Request.Path = $"/og/{id}";
                }
                else
                {
                    _logger.LogInformation(
                        "Scraper pass-through ({UA}) on {Path}", ua, path);
                }
            }

            // Make sure the response we send back is scraper-friendly:
            //  - No HSTS (some old crawlers choke on it)
            //  - No restrictive Cache-Control
            //  - Plain text/html content type respected
            //  - Allow indexing
            context.Response.OnStarting(() =>
            {
                // Robots OK
                context.Response.Headers["X-Robots-Tag"] = "all";
                // Friendly cache (10 min) — but only if nothing more specific was set by the page
                if (!context.Response.Headers.ContainsKey("Cache-Control")
                    || context.Response.Headers["Cache-Control"].ToString().Contains("no-store"))
                {
                    context.Response.Headers["Cache-Control"] = "public, max-age=600";
                }
                // HSTS is harmless for browsers but some scrapers refuse to follow http→https — remove it.
                context.Response.Headers.Remove("Strict-Transport-Security");
                return Task.CompletedTask;
            });
        }

        await _next(context);
    }

    public static bool IsScraper(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent)) return false;
        var lower = userAgent.ToLowerInvariant();
        foreach (var s in ScraperSubstrings)
            if (lower.Contains(s)) return true;
        return false;
    }
}

public static class ScraperRewriteMiddlewareExtensions
{
    public static IApplicationBuilder UseScraperRewrite(this IApplicationBuilder app)
        => app.UseMiddleware<ScraperRewriteMiddleware>();
}
