using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Options;
using MyHomePage.Abstractions;
using MyHomePage.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
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

    /// <inheritdoc />
    public async Task<long> GenerateOgImageAsync(
        string sourceImagePath,
        string targetOgPath,
        (double X, double Y)? cropFocus = null)
    {
        if (!File.Exists(sourceImagePath))
        {
            _logger.LogDebug("OG image skipped: source missing at {Source}", sourceImagePath);
            return 0;
        }

        try
        {
            using var source = await Image.LoadAsync(sourceImagePath);

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

            Directory.CreateDirectory(Path.GetDirectoryName(targetOgPath)!);
            var encoder = new JpegEncoder { Quality = 88 };
            await source.SaveAsJpegAsync(targetOgPath, encoder);

            var size = new FileInfo(targetOgPath).Length;
            _logger.LogInformation(
                "OG image generated {Path} ({KB} KB, focus {X:F2}/{Y:F2})",
                targetOgPath, size / 1024, focus.X, focus.Y);
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
