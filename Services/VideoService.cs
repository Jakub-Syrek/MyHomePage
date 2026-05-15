using Microsoft.Extensions.Options;
using MyHomePage.Abstractions;
using MyHomePage.Models;
using MyHomePage.Options;

namespace MyHomePage.Services;

/// <summary>
/// Orchestrates gallery-item operations by coordinating repository, file-storage,
/// compression, and location-extraction services.
/// SRP: business logic only — no raw I/O or codec knowledge.
/// DIP: depends on abstractions, not concrete implementations.
/// </summary>
public sealed class VideoService : IVideoService
{
    private readonly IVideoRepository _repository;
    private readonly IFileStorageService _storage;
    private readonly ICompressionStrategy _compression;
    private readonly ILocationExtractor _locationExtractor;
    private readonly VideoStorageOptions _options;
    private readonly ILogger<VideoService> _logger;

    public VideoService(
        IVideoRepository repository,
        IFileStorageService storage,
        ICompressionStrategy compression,
        ILocationExtractor locationExtractor,
        IOptions<VideoStorageOptions> options,
        ILogger<VideoService> logger)
    {
        _repository = repository;
        _storage = storage;
        _compression = compression;
        _locationExtractor = locationExtractor;
        _options = options.Value;
        _logger = logger;
    }

    // ── Queries ──────────────────────────────────────────────────────────────

    public Task<IReadOnlyList<Video>> GetAllVideosAsync() =>
        _repository.GetAllAsync();

    public async Task<IReadOnlyList<Video>> GetVideosByCategoryAsync(string category)
    {
        var all = await _repository.GetAllAsync();
        return all.Where(v => v.Category == category).ToList().AsReadOnly();
    }

    public Task<Video?> GetVideoByIdAsync(int id) =>
        _repository.GetByIdAsync(id);

    // ── Commands ─────────────────────────────────────────────────────────────

    public async Task<OperationResult<int>> UploadVideoAsync(VideoUploadRequest request)
    {
        var validation = Validate(request);
        if (!validation.IsSuccess)
            return OperationResult<int>.Failure(validation.Message);

        try
        {
            var videoId = _repository.GenerateNextId();
            _storage.EnsureVideoDirectoryExists(videoId);

            var mediaItems = new List<MediaItem>();
            string? primaryFileName = null;
            long totalSize = 0;
            double? latitude = request.Latitude;
            double? longitude = request.Longitude;

            // Process all files in order: first video → primary (compressed),
            // subsequent videos → media-XX.mp4, images → media-XX.jpg
            var orderedFiles = request.Files
                .OrderBy(f => IsImage(f.Name) ? 1 : 0) // videos first, then images
                .ToList();

            int videoCounter = 0;
            int imageCounter = 0;

            foreach (var file in orderedFiles)
            {
                var order = mediaItems.Count;

                if (IsImage(file.Name))
                {
                    imageCounter++;
                    var targetName = $"photo-{imageCounter:D2}.jpg";
                    var savedPath = await _storage.SaveImageWithResizeAsync(
                        file, videoId, _options.MaxFileSizeBytes, targetName);

                    var size = new FileInfo(savedPath).Length;
                    totalSize += size;
                    mediaItems.Add(MediaItem.Create(targetName, MediaType.Image, size, order));

                    if (latitude is null) // try GPS from this photo
                    {
                        var coords = _locationExtractor.TryExtract(savedPath);
                        if (coords is not null)
                        {
                            latitude = coords.Value.Latitude;
                            longitude = coords.Value.Longitude;
                        }
                    }
                    _logger.LogInformation(
                        "Item {Id}: saved image {Name} ({KB} KB)",
                        videoId, targetName, size / 1024);
                }
                else
                {
                    // Video file
                    videoCounter++;
                    bool isPrimary = primaryFileName is null;
                    var compressedName = isPrimary ? "video.mp4" : $"video-{videoCounter:D2}.mp4";

                    // Upload + compress
                    var originalPath = await _storage.SaveUploadedFileAsync(
                        file, videoId, _options.MaxFileSizeBytes,
                        targetFileName: $"orig-{videoCounter:D2}{Path.GetExtension(file.Name)}");

                    if (latitude is null) // try GPS from raw video
                    {
                        var coords = _locationExtractor.TryExtract(originalPath);
                        if (coords is not null)
                        {
                            latitude = coords.Value.Latitude;
                            longitude = coords.Value.Longitude;
                        }
                    }

                    var compressedPath = Path.Combine(
                        _storage.GetVideoDirectoryPath(videoId), compressedName);

                    var (finalName, finalSize) = await CompressAndFinalizeAsync(
                        originalPath, compressedPath, file.Size, videoId);

                    totalSize += finalSize;
                    mediaItems.Add(MediaItem.Create(finalName, MediaType.Video, finalSize, order));

                    if (isPrimary) primaryFileName = finalName;

                    _logger.LogInformation(
                        "Item {Id}: saved video {Name} ({MB} MB)",
                        videoId, finalName, finalSize / (1024 * 1024));
                }
            }

            if (primaryFileName is null && mediaItems.Count > 0)
                primaryFileName = mediaItems[0].FileName;

            if (primaryFileName is null)
                return OperationResult<int>.Failure("No usable files were uploaded.");

            var video = Video.Create(
                videoId,
                request.Title,
                request.Description,
                primaryFileName,
                request.Location,
                request.Category,
                totalSize,
                mediaItems,
                latitude,
                longitude);

            await _repository.SaveAsync(video);

            return OperationResult<int>.Success(
                videoId,
                $"Uploaded {mediaItems.Count} file(s) successfully" +
                (latitude.HasValue ? $" 📍 {latitude:F4}, {longitude:F4}" : ""));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Upload failed for '{Title}'", request.Title);
            return OperationResult<int>.Failure($"Upload error: {ex.Message}");
        }
    }

    public async Task<OperationResult> UpdateVideoAsync(int id, string title, string description, string? location)
    {
        return await UpdateVideoAsync(id, title, description, location, null, null);
    }

    public async Task<OperationResult> UpdateVideoAsync(
        int id, string title, string description, string? location,
        double? latitude, double? longitude)
    {
        _logger.LogInformation(
            "Updating item {Id}: title='{Title}', location='{Location}', GPS={Lat},{Lon}",
            id, title, location, latitude, longitude);

        var video = await _repository.GetByIdAsync(id);
        if (video is null)
        {
            _logger.LogWarning("Update failed: Item {Id} not found", id);
            return OperationResult.Failure($"Item {id} not found.");
        }

        video.Title = title;
        video.Description = description;
        video.Location = location;
        // Only overwrite coords if caller explicitly provided new ones
        if (latitude.HasValue && longitude.HasValue)
        {
            video.Latitude = latitude;
            video.Longitude = longitude;
        }

        try
        {
            await _repository.SaveAsync(video);
            return OperationResult.Success("Changes saved successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating item {Id}", id);
            return OperationResult.Failure($"Save error: {ex.Message}");
        }
    }

    public async Task<OperationResult> DeleteVideoAsync(int id)
    {
        _logger.LogInformation("Deleting item {Id}", id);

        try
        {
            var deleted = await _repository.DeleteAsync(id);
            if (deleted)
                return OperationResult.Success("Item deleted.");

            _logger.LogWarning("Delete failed: Item {Id} not found", id);
            return OperationResult.Failure($"Item {id} not found.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting item {Id}", id);
            return OperationResult.Failure($"Delete error: {ex.Message}");
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private bool IsImage(string fileName) =>
        MediaItem.DetectType(fileName) == MediaType.Image;

    private OperationResult Validate(VideoUploadRequest request)
    {
        if (request.Files.Count == 0)
            return OperationResult.Failure("No files were selected.");

        foreach (var file in request.Files)
        {
            if (file.Size > _options.MaxFileSizeBytes)
                return OperationResult.Failure(
                    $"File '{file.Name}' too large. Maximum: {_options.MaxFileSizeBytes / (1024 * 1024)} MB");

            var ext = Path.GetExtension(file.Name).ToLowerInvariant();
            if (!_options.AllowedExtensions.Contains(ext))
                return OperationResult.Failure(
                    $"Unsupported format for '{file.Name}'. Allowed: {string.Join(", ", _options.AllowedExtensions)}");
        }

        return OperationResult.Success();
    }

    private async Task<(string FileName, long Size)> CompressAndFinalizeAsync(
        string originalPath, string compressedPath, long originalSize, int videoId)
    {
        _logger.LogInformation("Item {Id}: starting compression with [{Strategy}]",
            videoId, _compression.Name);

        var compressed = await _compression.CompressAsync(originalPath, compressedPath);

        if (compressed && File.Exists(compressedPath))
        {
            var compressedSize = new FileInfo(compressedPath).Length;
            try { File.Delete(originalPath); } catch { /* keep original on failure */ }

            _logger.LogInformation("Item {Id}: {Old} MB → {New} MB",
                videoId,
                originalSize / (1024 * 1024),
                compressedSize / (1024 * 1024));

            return (Path.GetFileName(compressedPath), compressedSize);
        }

        _logger.LogWarning("Item {Id}: compression failed, keeping original", videoId);
        return (Path.GetFileName(originalPath), originalSize);
    }
}
