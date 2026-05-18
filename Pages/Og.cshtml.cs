using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MyHomePage.Abstractions;
using MyHomePage.Models;

namespace MyHomePage.Pages;

/// <summary>
/// Lightweight server-rendered Open Graph preview for a single gallery item.
/// Designed for social-network scrapers (Facebook, Twitter, LinkedIn, …) which
/// don't run JavaScript — Blazor Server's HeadContent is invisible to them.
///
/// Route: GET /og/{id}
/// Humans are auto-redirected via &lt;meta http-equiv="refresh"&gt; to /item/{id}.
/// Scrapers parse the OG meta tags and stop there.
/// </summary>
public sealed class OgModel : PageModel
{
    private readonly IVideoService _videoService;
    private readonly IFileStorageService _storage;

    public OgModel(IVideoService videoService, IFileStorageService storage)
    {
        _videoService = videoService;
        _storage = storage;
    }

    public string Title { get; set; } = "My Mountain Adventures";
    public string Description { get; set; } = "Mountain adventure photos and videos.";
    public string CanonicalUrl { get; set; } = "";
    public string ImageUrl { get; set; } = "";
    public string? VideoUrl { get; set; }
    public string PublishedAt { get; set; } = DateTime.UtcNow.ToString("o");
    public bool HasGps { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public bool HasImage { get; set; }
    public bool HasVideo { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var item = await _videoService.GetVideoByIdAsync(id);
        var origin = $"{Request.Scheme}://{Request.Host}";

        if (item is null)
        {
            // Still return a valid 200 with default OG so social scrapers don't blow up
            CanonicalUrl = $"{origin}/";
            ImageUrl = $"{origin}/images/mountains-bg.jpg";
            return Page();
        }

        CanonicalUrl = $"{origin}/item/{id}";
        Title = item.Title;

        var desc = (item.Description ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
        if (desc.Length > 200) desc = desc.Substring(0, 200) + "…";
        if (string.IsNullOrWhiteSpace(desc))
            desc = string.IsNullOrEmpty(item.Location)
                ? "Mountain adventure photos and videos."
                : $"📍 {item.Location}";
        Description = desc;

        var media = item.GetAllMedia();
        var firstImage = media.FirstOrDefault(m => m.Type == MediaType.Image);
        var firstVideo = media.FirstOrDefault(m => m.Type == MediaType.Video);

        // Prefer the pre-cropped 1200x630 og.jpg if VideoService or the
        // Strava importer generated one — scrapers then render the exact
        // framing we chose instead of running their own centre-crop.
        var ogJpgPath = Path.Combine(_storage.GetVideoDirectoryPath(id), "og.jpg");
        // System.IO.File qualified because PageModel.File(byte[], string)
        // shadows the type alias inside a Razor Page model context.
        var hasDedicatedOgImage = System.IO.File.Exists(ogJpgPath);

        ImageUrl = hasDedicatedOgImage
            ? $"{origin}/videos/{id}/og.jpg"
            : firstImage is not null
                ? $"{origin}/videos/{id}/{firstImage.FileName}"
                : $"{origin}/images/mountains-bg.jpg";
        HasImage = hasDedicatedOgImage || firstImage is not null;

        if (firstVideo is not null)
        {
            VideoUrl = $"{origin}/videos/{id}/{firstVideo.FileName}";
            HasVideo = true;
        }

        PublishedAt = item.UploadedAt.ToString("o");

        if (item.HasCoordinates)
        {
            HasGps = true;
            Latitude = item.Latitude;
            Longitude = item.Longitude;
        }

        return Page();
    }
}
