using System.Globalization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Options;
using MyHomePage.Abstractions;
using MyHomePage.Options;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace MyHomePage.Services;

/// <summary>
/// Handles all file-system operations for media storage.
/// Single Responsibility Principle (S in SOLID): owns the "where/how files live" concern.
///
/// Storage root resolution order:
///   1. Env var VIDEO_STORAGE_ROOT (Railway production)
///   2. VideoStorageOptions.StorageRoot (appsettings)
///   3. {webroot}/videos (default for local dev)
/// </summary>
public sealed class FileStorageService : IFileStorageService
{
    private readonly IWebHostEnvironment _environment;
    private readonly VideoStorageOptions _options;
    private readonly ILogger<FileStorageService> _logger;
    private readonly string _videosRoot;

    public FileStorageService(
        IWebHostEnvironment environment,
        IOptions<VideoStorageOptions> options,
        ILogger<FileStorageService> logger)
    {
        _environment = environment;
        _options = options.Value;
        _logger = logger;

        _videosRoot = ResolveStorageRoot();
        Directory.CreateDirectory(_videosRoot);
        _logger.LogInformation("Video storage root: {Root}", _videosRoot);
    }

    private string ResolveStorageRoot()
    {
        var envRoot = Environment.GetEnvironmentVariable("VIDEO_STORAGE_ROOT");
        if (!string.IsNullOrWhiteSpace(envRoot)) return Path.GetFullPath(envRoot);
        if (!string.IsNullOrWhiteSpace(_options.StorageRoot)) return Path.GetFullPath(_options.StorageRoot);
        return Path.Combine(_environment.WebRootPath, _options.VideosFolder);
    }

    public string GetVideosRootPath() => _videosRoot;
    public string GetVideoDirectoryPath(int id) => Path.Combine(_videosRoot, id.ToString());
    public string GetMetadataFilePath(int id) => Path.Combine(GetVideoDirectoryPath(id), "metadata.json");
    public void EnsureVideoDirectoryExists(int id) => Directory.CreateDirectory(GetVideoDirectoryPath(id));
    public bool VideoDirectoryExists(int id) => Directory.Exists(GetVideoDirectoryPath(id));

    public async Task<string> SaveUploadedFileAsync(
        IBrowserFile file,
        int videoId,
        long maxFileSizeBytes,
        string? targetFileName = null)
    {
        EnsureVideoDirectoryExists(videoId);

        var ext = Path.GetExtension(file.Name).ToLowerInvariant();
        var fileName = targetFileName ?? $"original{ext}";
        var filePath = Path.Combine(GetVideoDirectoryPath(videoId), fileName);

        await using var readStream = file.OpenReadStream(maxFileSizeBytes);
        await using var writeStream = new FileStream(filePath, FileMode.Create);
        await readStream.CopyToAsync(writeStream);

        return filePath;
    }

    public async Task<string> SaveImageWithResizeAsync(
        IBrowserFile file,
        int videoId,
        long maxFileSizeBytes,
        string targetFileName,
        int maxDimension = 2560,
        int jpegQuality = 85)
    {
        EnsureVideoDirectoryExists(videoId);
        var filePath = Path.Combine(GetVideoDirectoryPath(videoId), targetFileName);

        try
        {
            await using var readStream = file.OpenReadStream(maxFileSizeBytes);
            using var image = await Image.LoadAsync(readStream);

            // Bake the EXIF rotation into pixels and drop the metadata
            // — phone cameras routinely save portrait shots with
            // Orientation=6 ("rotate 270 CW") and Facebook is one of
            // many viewers that double-rotates the already-upright
            // pixels because it sees the leftover EXIF tag. Stripping
            // here means every downstream consumer (gallery, lightbox,
            // og.jpg pipeline) sees a deterministic upright JPEG.
            image.Mutate(x => x.AutoOrient());
            image.Metadata.ExifProfile = null;
            image.Metadata.IptcProfile = null;
            image.Metadata.XmpProfile = null;

            // Resize while preserving aspect ratio, only if larger than maxDimension
            if (image.Width > maxDimension || image.Height > maxDimension)
            {
                image.Mutate(x => x.Resize(new ResizeOptions
                {
                    Size = new Size(maxDimension, maxDimension),
                    Mode = ResizeMode.Max
                }));
                _logger.LogInformation(
                    "Image {File} resized to fit within {Max}px (was {W}x{H})",
                    file.Name, maxDimension, image.Width, image.Height);
            }

            var encoder = new JpegEncoder { Quality = jpegQuality };
            await image.SaveAsJpegAsync(filePath, encoder);
            return filePath;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Image processing failed for {File}, falling back to raw save", file.Name);

            // Fallback: save raw bytes if ImageSharp can't decode (e.g. HEIC)
            await using var readStream = file.OpenReadStream(maxFileSizeBytes);
            await using var writeStream = new FileStream(filePath, FileMode.Create);
            await readStream.CopyToAsync(writeStream);
            return filePath;
        }
    }

    public async Task DeleteVideoDirectoryAsync(int id)
    {
        var dir = GetVideoDirectoryPath(id);
        if (Directory.Exists(dir))
            await Task.Run(() => Directory.Delete(dir, recursive: true));
    }

    /// <summary>
    /// Open Graph / Facebook recommended image dimensions: 1200x630, 1.91:1.
    /// Producing this size up-front means scrapers (FB, LinkedIn, Slack,
    /// WhatsApp) display the preview as-cropped instead of running their
    /// own centre-crop heuristic that often chops off heads or hands.
    /// </summary>
    private const int OgWidth = 1200;
    private const int OgHeight = 630;
    private const double OgAspect = (double)OgWidth / OgHeight; // 1.9047...

    /// <summary>
    /// Lazily-resolved system font used for the OG stats overlay. We pick
    /// the first available match from a short whitelist (DejaVu first,
    /// because the Dockerfile installs fonts-dejavu-core specifically for
    /// this code path). When no system font is available the overlay is
    /// silently skipped — the plain crop still ships, just without text.
    /// </summary>
    private static readonly Lazy<FontFamily?> _ogFont = new(() =>
    {
        foreach (var name in new[]
        {
            "DejaVu Sans", "Inter", "Roboto", "Arial",
            "Liberation Sans", "Segoe UI", "Helvetica"
        })
        {
            if (SystemFonts.TryGet(name, out var family)) return family;
        }
        return SystemFonts.Families.FirstOrDefault();
    });

    /// <inheritdoc />
    public async Task<long> GenerateOgImageAsync(
        string sourceImagePath,
        string targetOgPath,
        (double X, double Y)? cropFocus = null,
        OgOverlay? overlay = null)
    {
        if (!File.Exists(sourceImagePath))
        {
            _logger.LogDebug("OG image skipped: source missing at {Source}", sourceImagePath);
            return 0;
        }

        try
        {
            using var source = await Image.LoadAsync(sourceImagePath);

            // Force the in-memory pixel buffer to match the upright
            // orientation declared by any EXIF tag on the source, then
            // wipe every metadata profile so the saved og.jpg cannot
            // accidentally carry a second rotation hint that a viewer
            // (Facebook, in particular) would apply on top of pixels
            // that are already in their final landscape orientation.
            source.Mutate(ctx => ctx.AutoOrient());
            source.Metadata.ExifProfile = null;
            source.Metadata.IptcProfile = null;
            source.Metadata.XmpProfile = null;
            source.Metadata.IccProfile = null;

            var focus = cropFocus ?? (0.5, 0.5);
            var (cropX, cropY, cropW, cropH) = ResolveCropWindow(
                source.Width, source.Height, focus.X, focus.Y);

            source.Mutate(ctx =>
            {
                ctx.Crop(new Rectangle(cropX, cropY, cropW, cropH));
                ctx.Resize(new ResizeOptions
                {
                    Size = new Size(OgWidth, OgHeight),
                    Mode = ResizeMode.Stretch
                });
            });

            if (overlay is not null)
            {
                DrawOverlay(source, overlay);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetOgPath)!);
            var encoder = new JpegEncoder { Quality = 88 };
            await source.SaveAsJpegAsync(targetOgPath, encoder);

            var size = new FileInfo(targetOgPath).Length;
            _logger.LogInformation(
                "OG image generated {Path} ({KB} KB, focus {X:F2}/{Y:F2}, overlay {HasOverlay})",
                targetOgPath, size / 1024, focus.X, focus.Y, overlay is not null);
            return size;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "OG image generation failed for {Source}", sourceImagePath);
            return 0;
        }
    }

    /// <summary>
    /// Paints a translucent bottom strip with the supplied training /
    /// metadata. Layout: top row = activity label + date / location,
    /// bottom row = up to four big-number stats separated by dots.
    /// Silently no-ops when no system font is available so we still
    /// produce a clean cropped image instead of throwing.
    /// </summary>
    private void DrawOverlay(Image image, OgOverlay overlay)
    {
        var family = _ogFont.Value;
        if (family is null)
        {
            _logger.LogDebug("No system font available — OG overlay text skipped");
            return;
        }

        var titleFont = family.Value.CreateFont(28, FontStyle.Regular);
        var statFont = family.Value.CreateFont(46, FontStyle.Bold);
        var statLabelFont = family.Value.CreateFont(18, FontStyle.Regular);

        var stripHeight = 170;
        var stripTop = OgHeight - stripHeight;

        image.Mutate(ctx =>
        {
            // Translucent dark gradient strip — solid black at 70 % alpha
            // is good enough at this size; a true gradient would need
            // SixLabors.ImageSharp.Drawing.LinearGradientBrush which is
            // overkill for the readability boost it gives here.
            ctx.Fill(
                Color.FromRgba(0, 0, 0, 175),
                new RectangleF(0, stripTop, OgWidth, stripHeight));

            var marginLeft = 36f;
            var marginRight = 36f;

            // ── Top row: activity label (left) + date / location (right)
            var topRowY = stripTop + 14f;
            var topLabelParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(overlay.ActivityLabel))
                topLabelParts.Add(overlay.ActivityLabel!);
            if (!string.IsNullOrWhiteSpace(overlay.Location))
                topLabelParts.Add(overlay.Location!);
            var topLabel = string.Join("  ·  ", topLabelParts);
            if (!string.IsNullOrEmpty(topLabel))
            {
                ctx.DrawText(
                    topLabel, titleFont, Color.FromRgba(255, 255, 255, 235),
                    new PointF(marginLeft, topRowY));
            }
            if (overlay.CapturedAt is DateTime captured)
            {
                var dateText = captured.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);
                var dateOptions = new RichTextOptions(titleFont)
                {
                    Origin = new PointF(OgWidth - marginRight, topRowY),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top
                };
                ctx.DrawText(dateOptions, dateText, Color.FromRgba(255, 255, 255, 200));
            }

            // ── Stats row: up to 4 big numbers separated by a thin divider
            var statY = stripTop + 70f;
            var stats = BuildStatsList(overlay).Take(4).ToList();
            if (stats.Count == 0) return;

            var slotWidth = (OgWidth - marginLeft - marginRight) / stats.Count;
            for (var i = 0; i < stats.Count; i++)
            {
                var stat = stats[i];
                var slotCentre = marginLeft + slotWidth * (i + 0.5f);

                var valueOptions = new RichTextOptions(statFont)
                {
                    Origin = new PointF(slotCentre, statY),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Top
                };
                ctx.DrawText(valueOptions, stat.Value, Color.White);

                var labelOptions = new RichTextOptions(statLabelFont)
                {
                    Origin = new PointF(slotCentre, statY + 56f),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Top
                };
                ctx.DrawText(labelOptions, stat.Label, Color.FromRgba(255, 255, 255, 195));

                // Divider between slots (skip after the last).
                if (i < stats.Count - 1)
                {
                    var dividerX = marginLeft + slotWidth * (i + 1);
                    ctx.Fill(
                        Color.FromRgba(255, 255, 255, 70),
                        new RectangleF(
                            dividerX, statY + 4f, 1f, 78f));
                }
            }
        });
    }

    /// <summary>
    /// Picks the four most informative stat slots out of the overlay
    /// payload in a fixed priority order so the look stays consistent
    /// across activity types.
    /// </summary>
    private static IEnumerable<(string Value, string Label)> BuildStatsList(OgOverlay overlay)
    {
        if (overlay.DistanceMeters is > 0)
            yield return (FormatKm(overlay.DistanceMeters.Value), "DISTANCE");
        if (overlay.Duration is { } duration && duration > TimeSpan.Zero)
            yield return (FormatDuration(duration), "TIME");
        if (overlay.PaceSecondsPerKm is > 0)
            yield return (FormatPace(overlay.PaceSecondsPerKm.Value), "PACE");
        if (overlay.Calories is > 0)
            yield return (overlay.Calories.Value.ToString(CultureInfo.InvariantCulture), "KCAL");
        if (overlay.ElevationGainMeters is > 0)
            yield return ($"{overlay.ElevationGainMeters.Value:F0} m", "ELEVATION");
    }

    private static string FormatKm(double meters) =>
        meters >= 1000
            ? $"{(meters / 1000):F1} km"
            : $"{meters:F0} m";

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
            return $"{(int)duration.TotalHours}:{duration.Minutes:D2}";
        return $"{duration.Minutes}:{duration.Seconds:D2}";
    }

    private static string FormatPace(double secondsPerKm)
    {
        var minutes = (int)(secondsPerKm / 60);
        var seconds = (int)Math.Round(secondsPerKm - minutes * 60);
        return $"{minutes}:{seconds:D2}/km";
    }

    /// <summary>
    /// Computes the largest 1.91:1 rectangle that fits inside the source
    /// image, then shifts it so the centre matches the supplied focal
    /// point (clamped to stay inside the source).
    /// </summary>
    private static (int X, int Y, int Width, int Height) ResolveCropWindow(
        int sourceWidth, int sourceHeight, double focusX, double focusY)
    {
        int cropW, cropH;
        var sourceAspect = (double)sourceWidth / sourceHeight;
        if (sourceAspect >= OgAspect)
        {
            // Source is wider than 1.91:1 — height is the limiting axis.
            cropH = sourceHeight;
            cropW = (int)Math.Round(sourceHeight * OgAspect);
        }
        else
        {
            // Source is taller than 1.91:1 — width is the limiting axis.
            cropW = sourceWidth;
            cropH = (int)Math.Round(sourceWidth / OgAspect);
        }

        var clampedFocusX = Math.Clamp(focusX, 0.0, 1.0);
        var clampedFocusY = Math.Clamp(focusY, 0.0, 1.0);

        var cropX = (int)Math.Round(clampedFocusX * sourceWidth - cropW / 2.0);
        var cropY = (int)Math.Round(clampedFocusY * sourceHeight - cropH / 2.0);
        cropX = Math.Clamp(cropX, 0, sourceWidth - cropW);
        cropY = Math.Clamp(cropY, 0, sourceHeight - cropH);
        return (cropX, cropY, cropW, cropH);
    }

    /// <inheritdoc />
    public async Task<long> CopyWwwRootFileToVideoAsync(
        string wwwRootRelativePath,
        int videoId,
        string targetFileName)
    {
        if (string.IsNullOrWhiteSpace(wwwRootRelativePath)) return 0;
        if (string.IsNullOrWhiteSpace(targetFileName)) return 0;

        var safeRelative = wwwRootRelativePath.Replace('\\', '/').TrimStart('/');
        var sourcePath = Path.Combine(_environment.WebRootPath,
            safeRelative.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(sourcePath))
        {
            _logger.LogWarning(
                "Could not seed video {Id} from {Source}: file does not exist",
                videoId, sourcePath);
            return 0;
        }

        EnsureVideoDirectoryExists(videoId);
        var destinationPath = Path.Combine(GetVideoDirectoryPath(videoId), targetFileName);

        await using (var source = File.OpenRead(sourcePath))
        await using (var destination = File.Create(destinationPath))
        {
            await source.CopyToAsync(destination);
            await destination.FlushAsync();
        }

        // FileInfo.Length is read AFTER the streams are disposed so the
        // OS has committed the buffered bytes — otherwise Windows still
        // reports 0 for the freshly-created handle.
        var size = new FileInfo(destinationPath).Length;
        _logger.LogInformation(
            "Seeded item {Id} with {File} ({KB} KB) from {Source}",
            videoId, targetFileName, size / 1024, safeRelative);
        return size;
    }
}
