using System.Text.RegularExpressions;

namespace MyHomePage.Services;

/// <summary>
/// When a social-media scraper (Facebook, Twitter, LinkedIn, Discord, etc.)
/// requests <c>/item/{id}</c>, internally rewrite the path to <c>/og/{id}</c>
/// — a static, server-rendered Razor Page with proper Open Graph meta tags.
///
/// Why: Blazor Server's &lt;HeadContent&gt; is rendered client-side after the
/// SignalR connection; scrapers don't execute JS and see only the default
/// head from _Host.cshtml, which breaks rich previews.
///
/// The browser's URL stays /item/{id} for both users and bots (rewrite, not
/// redirect), so existing links keep working.
/// </summary>
public sealed class ScraperRewriteMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ScraperRewriteMiddleware> _logger;

    // Match /item/{id} (digits only). Optional trailing slash / query string.
    private static readonly Regex ItemRoute = new(
        @"^/item/(\d+)/?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Known scraper user-agents. Lower-case substring match.
    private static readonly string[] ScraperSubstrings =
    {
        "facebookexternalhit",
        "facebookcatalog",
        "meta-externalagent",
        "facebot",
        "twitterbot",
        "linkedinbot",
        "slackbot",
        "discordbot",
        "telegrambot",
        "whatsapp",
        "skypeuripreview",
        "pinterestbot",
        "embedly",
        "redditbot",
        "vkshare",
        "applebot",          // iMessage previews
        "google-inspectiontool",
        "googlebot",         // SEO + previews
        "bingbot",
        "duckduckbot",
        "yahoo!slurp"
    };

    public ScraperRewriteMiddleware(RequestDelegate next, ILogger<ScraperRewriteMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value;
        if (path is not null && context.Request.Method == "GET")
        {
            var match = ItemRoute.Match(path);
            if (match.Success && IsScraper(context.Request.Headers.UserAgent.ToString()))
            {
                var id = match.Groups[1].Value;
                _logger.LogInformation(
                    "Scraper detected ({UA}) — rewriting {From} → /og/{Id}",
                    context.Request.Headers.UserAgent.ToString(), path, id);
                context.Request.Path = $"/og/{id}";
            }
        }

        await _next(context);
    }

    private static bool IsScraper(string userAgent)
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
