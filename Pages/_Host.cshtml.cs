using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MyHomePage.Models;
using MyHomePage.Services;
using System.Text.RegularExpressions;

namespace MyHomePage.Pages;

[AllowAnonymous]
public class HostModel : PageModel
{
    private readonly VideoService _videoService;

    public HostModel(VideoService videoService)
    {
        _videoService = videoService;
    }

    public Video? OgVideo { get; set; }
    public string OgBaseUrl { get; set; } = "";

    public async Task OnGetAsync()
    {
        // Detect HTTPS from X-Forwarded-Proto header (set by ngrok/proxies)
        var scheme = Request.Headers["X-Forwarded-Proto"].FirstOrDefault() ?? Request.Scheme;
        // Ensure HTTPS for social media crawlers
        if (scheme.Equals("https", StringComparison.OrdinalIgnoreCase) || Request.Host.Host.Contains("ngrok"))
        {
            scheme = "https";
        }
        OgBaseUrl = $"{scheme}://{Request.Host}";

        var path = Request.Path.Value ?? "";

        var videoMatch = Regex.Match(path, @"^/video/(\d+)/?$");
        if (videoMatch.Success && int.TryParse(videoMatch.Groups[1].Value, out var videoId))
        {
            OgVideo = await _videoService.GetVideoByIdAsync(videoId);
            if (OgVideo != null)
            {
                await _videoService.EnsureThumbnailExistsAsync(videoId);
            }
        }
    }
}
