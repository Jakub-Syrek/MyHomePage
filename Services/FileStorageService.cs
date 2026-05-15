using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Options;
using MyHomePage.Abstractions;
using MyHomePage.Options;

namespace MyHomePage.Services;

/// <summary>
/// Handles all file-system operations for video storage.
/// Single Responsibility Principle (S in SOLID): this class owns only the "where/how files live" concern.
///
/// Storage root resolution order:
///   1. VideoStorageOptions.StorageRoot (from appsettings or env var VIDEO_STORAGE_ROOT)
///   2. {webroot}/videos (default for local dev)
///
/// On Railway: set VIDEO_STORAGE_ROOT=/data/videos and mount a volume at /data.
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
        // 1. Env var (Railway production)
        var envRoot = Environment.GetEnvironmentVariable("VIDEO_STORAGE_ROOT");
        if (!string.IsNullOrWhiteSpace(envRoot))
            return Path.GetFullPath(envRoot);

        // 2. Options-configured absolute path
        if (!string.IsNullOrWhiteSpace(_options.StorageRoot))
            return Path.GetFullPath(_options.StorageRoot);

        // 3. Default: wwwroot/videos
        return Path.Combine(_environment.WebRootPath, _options.VideosFolder);
    }

    public string GetVideosRootPath() => _videosRoot;

    public string GetVideoDirectoryPath(int id) =>
        Path.Combine(GetVideosRootPath(), id.ToString());

    public string GetMetadataFilePath(int id) =>
        Path.Combine(GetVideoDirectoryPath(id), "metadata.json");

    public void EnsureVideoDirectoryExists(int id) =>
        Directory.CreateDirectory(GetVideoDirectoryPath(id));

    public bool VideoDirectoryExists(int id) =>
        Directory.Exists(GetVideoDirectoryPath(id));

    public async Task<string> SaveUploadedFileAsync(IBrowserFile file, int videoId, long maxFileSizeBytes)
    {
        EnsureVideoDirectoryExists(videoId);

        var ext = Path.GetExtension(file.Name).ToLowerInvariant();
        var fileName = $"original{ext}";
        var filePath = Path.Combine(GetVideoDirectoryPath(videoId), fileName);

        await using var readStream = file.OpenReadStream(maxFileSizeBytes);
        await using var writeStream = new FileStream(filePath, FileMode.Create);
        await readStream.CopyToAsync(writeStream);

        return filePath;
    }

    public async Task DeleteVideoDirectoryAsync(int id)
    {
        var dir = GetVideoDirectoryPath(id);
        if (Directory.Exists(dir))
            await Task.Run(() => Directory.Delete(dir, recursive: true));
    }
}
