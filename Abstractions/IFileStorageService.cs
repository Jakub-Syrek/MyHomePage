using Microsoft.AspNetCore.Components.Forms;

namespace MyHomePage.Abstractions;

/// <summary>
/// Abstraction for file system operations related to media storage.
/// Follows the Interface Segregation Principle (I in SOLID).
/// </summary>
public interface IFileStorageService
{
    string GetVideosRootPath();
    string GetVideoDirectoryPath(int id);
    string GetMetadataFilePath(int id);

    void EnsureVideoDirectoryExists(int id);
    bool VideoDirectoryExists(int id);

    /// <summary>
    /// Streams the browser file to disk in the item's directory. Uses
    /// <paramref name="targetFileName"/> if provided, otherwise <c>original{ext}</c>.
    /// Returns the saved file path.
    /// </summary>
    Task<string> SaveUploadedFileAsync(
        IBrowserFile file,
        int videoId,
        long maxFileSizeBytes,
        string? targetFileName = null);

    /// <summary>
    /// Saves an image with optional resize to <paramref name="maxDimension"/> pixels
    /// (preserving aspect ratio) and JPEG re-encoding at quality 85. Returns the
    /// final file path.
    /// </summary>
    Task<string> SaveImageWithResizeAsync(
        IBrowserFile file,
        int videoId,
        long maxFileSizeBytes,
        string targetFileName,
        int maxDimension = 2560,
        int jpegQuality = 85);

    Task DeleteVideoDirectoryAsync(int id);

    /// <summary>
    /// Copies a static asset shipped with the app (under <c>wwwroot/</c>) into
    /// the video directory of the given gallery item. Used to seed
    /// Strava-imported placeholders with a real cover image so they render
    /// like normal photo uploads instead of empty media tiles.
    /// </summary>
    /// <param name="wwwRootRelativePath">Path relative to <c>wwwroot</c> (e.g. <c>images/running-bg.jpg</c>).</param>
    /// <param name="videoId">Target gallery item id.</param>
    /// <param name="targetFileName">File name to write under that item's directory.</param>
    /// <returns>Byte size of the written copy, or 0 when the source did not exist.</returns>
    Task<long> CopyWwwRootFileToVideoAsync(
        string wwwRootRelativePath,
        int videoId,
        string targetFileName);

    /// <summary>
    /// Generates a Facebook / Open Graph friendly image (1200x630, 1.91:1
    /// aspect ratio) from the supplied source image and writes it next to
    /// the source. The source is centre-cropped by default; a normalised
    /// <paramref name="cropFocus"/> point can shift the crop window so the
    /// operator's framing is preserved.
    /// </summary>
    /// <param name="sourceImagePath">Absolute path to an existing JPEG/PNG/WEBP.</param>
    /// <param name="targetOgPath">Absolute path to write the 1200x630 JPEG.</param>
    /// <param name="cropFocus">
    /// Optional focal point as (xFraction, yFraction) in [0,1] — the centre
    /// of the crop window. <c>null</c> defaults to (0.5, 0.5) (geometric
    /// centre).
    /// </param>
    /// <param name="overlay">
    /// Optional stats payload. When supplied (and a system font is
    /// available), the cropped image is composited with a translucent
    /// bottom strip carrying the activity label, distance, duration,
    /// pace, calories etc. so the Facebook preview shows the numbers
    /// without the operator having to type them into the post body.
    /// </param>
    /// <returns>Byte size of the generated OG image, or 0 on failure.</returns>
    Task<long> GenerateOgImageAsync(
        string sourceImagePath,
        string targetOgPath,
        (double X, double Y)? cropFocus = null,
        OgOverlay? overlay = null);
}

/// <summary>
/// Optional stats panel rendered onto the bottom of an Open Graph image.
/// Every field is nullable — only populated entries are drawn so the
/// strip stays compact for plain photo uploads while still surfacing the
/// full training summary for Strava-imported items.
/// </summary>
public sealed record OgOverlay
{
    /// <summary>Activity-type display label (e.g. "Run", "Ride", "Mountains").</summary>
    public string? ActivityLabel { get; init; }

    /// <summary>Distance covered, in metres. Rendered as kilometres.</summary>
    public double? DistanceMeters { get; init; }

    /// <summary>Moving time. Rendered as <c>HH:MM</c> or <c>MM:SS</c>.</summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>Pace as seconds per kilometre. Rendered as <c>m:ss/km</c>.</summary>
    public double? PaceSecondsPerKm { get; init; }

    /// <summary>Total calories burned during the session.</summary>
    public int? Calories { get; init; }

    /// <summary>Total elevation gain, in metres.</summary>
    public double? ElevationGainMeters { get; init; }

    /// <summary>Date the activity / photo was captured.</summary>
    public DateTime? CapturedAt { get; init; }

    /// <summary>Free-form location label (e.g. "Kraków, Poland").</summary>
    public string? Location { get; init; }
}
