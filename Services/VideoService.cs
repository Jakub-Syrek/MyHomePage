using Microsoft.AspNetCore.Components.Forms;
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
    private readonly IDateTakenExtractor _dateExtractor;
    private readonly VideoStorageOptions _options;
    private readonly ILogger<VideoService> _logger;

    public VideoService(
        IVideoRepository repository,
        IFileStorageService storage,
        ICompressionStrategy compression,
        ILocationExtractor locationExtractor,
        IDateTakenExtractor dateExtractor,
        IOptions<VideoStorageOptions> options,
        ILogger<VideoService> logger)
    {
        _repository = repository;
        _storage = storage;
        _compression = compression;
        _locationExtractor = locationExtractor;
        _dateExtractor = dateExtractor;
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
            DateTime? earliestCapture = null;

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

                    // First image of the upload becomes the Facebook / OG
                    // preview source. A 1200x630 centre-crop is written to
                    // og.jpg so scrapers display a deterministic preview
                    // instead of running their own crop heuristic.
                    if (imageCounter == 1)
                    {
                        var ogPath = Path.Combine(
                            _storage.GetVideoDirectoryPath(videoId), "og.jpg");
                        await _storage.GenerateOgImageAsync(savedPath, ogPath);
                    }

                    if (latitude is null) // try GPS from this photo
                    {
                        var coords = _locationExtractor.TryExtract(savedPath);
                        if (coords is not null)
                        {
                            latitude = coords.Value.Latitude;
                            longitude = coords.Value.Longitude;
                        }
                    }
                    TrackEarliestCapture(savedPath, ref earliestCapture);
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
                    TrackEarliestCapture(originalPath, ref earliestCapture);

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

            // Prefer the earliest "date taken" found in the uploaded files,
            // falling back to the current time set by Video.Create when no
            // file carried a usable timestamp.
            if (earliestCapture.HasValue)
            {
                video.UploadedAt = earliestCapture.Value;
                _logger.LogInformation(
                    "Item {Id}: UploadedAt set from file metadata to {Date:o}",
                    video.Id, video.UploadedAt);
            }

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

    public async Task<OperationResult> AppendMediaAsync(
        int videoId,
        IReadOnlyList<IBrowserFile> files)
    {
        if (files.Count == 0)
            return OperationResult.Failure("No files were selected.");

        var validation = ValidateFiles(files);
        if (!validation.IsSuccess) return validation;

        var video = await _repository.GetByIdAsync(videoId);
        if (video is null)
            return OperationResult.Failure($"Gallery item {videoId} not found.");

        try
        {
            _storage.EnsureVideoDirectoryExists(videoId);
            var media = video.Media.ToList();
            var imageCount = media.Count(m => m.Type == MediaType.Image);
            var videoCount = media.Count(m => m.Type == MediaType.Video);
            var totalSizeDelta = 0L;
            var ordered = files.OrderBy(f => IsImage(f.Name) ? 1 : 0).ToList();

            foreach (var file in ordered)
            {
                var order = media.Count;
                if (IsImage(file.Name))
                {
                    imageCount++;
                    var item = await StoreImageAsync(file, videoId, imageCount, order);
                    media.Add(item);
                    totalSizeDelta += item.SizeBytes;
                }
                else
                {
                    videoCount++;
                    var (item, primary) = await StoreVideoAsync(
                        file, videoId, videoCount, order, hasPrimary: !string.IsNullOrEmpty(video.FileName));
                    media.Add(item);
                    totalSizeDelta += item.SizeBytes;
                    if (primary is not null) video.FileName = primary;
                }
            }

            video.Media = media;
            if (string.IsNullOrEmpty(video.FileName) && media.Count > 0)
                video.FileName = media[0].FileName;
            video.FileSizeBytes += totalSizeDelta;

            await _repository.SaveAsync(video);
            return OperationResult.Success($"Added {files.Count} file(s) to item {videoId}.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Append failed for item {Id}", videoId);
            return OperationResult.Failure($"Append error: {ex.Message}");
        }
    }

    public async Task<OperationResult> RemoveMediaAsync(int videoId, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return OperationResult.Failure("File name was not provided.");

        var video = await _repository.GetByIdAsync(videoId);
        if (video is null)
            return OperationResult.Failure($"Gallery item {videoId} not found.");

        var media = video.Media.ToList();
        var entry = media.FirstOrDefault(m =>
            string.Equals(m.FileName, fileName, StringComparison.Ordinal));
        if (entry is null)
            return OperationResult.Failure(
                $"Media file '{fileName}' is not attached to item {videoId}.");

        try
        {
            var absolutePath = Path.Combine(
                _storage.GetVideoDirectoryPath(videoId), entry.FileName);
            if (File.Exists(absolutePath))
            {
                File.Delete(absolutePath);
                _logger.LogInformation(
                    "Item {VideoId}: removed media {FileName} ({KB} KB)",
                    videoId, entry.FileName, entry.SizeBytes / 1024);
            }
            else
            {
                _logger.LogWarning(
                    "Item {VideoId}: media {FileName} not on disk - removed from metadata only",
                    videoId, entry.FileName);
            }

            media.Remove(entry);
            for (var i = 0; i < media.Count; i++)
                media[i].Order = i;

            video.Media = media;
            video.FileSizeBytes = Math.Max(0, video.FileSizeBytes - entry.SizeBytes);

            if (string.Equals(video.FileName, entry.FileName, StringComparison.Ordinal))
            {
                video.FileName = media.Count > 0
                    ? media[0].FileName
                    : string.Empty;
            }

            await _repository.SaveAsync(video);
            return OperationResult.Success(
                $"Removed '{entry.FileName}' from item {videoId}.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error removing media {FileName} from item {VideoId}",
                entry.FileName, videoId);
            return OperationResult.Failure($"Remove error: {ex.Message}");
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

    private static bool IsImage(string fileName) =>
        MediaItem.DetectType(fileName) == MediaType.Image;

    /// <summary>
    /// Reads the capture timestamp from the given file and remembers the
    /// earliest one across the whole upload. The collection's
    /// <see cref="Video.UploadedAt"/> is then anchored to that timestamp
    /// instead of "now" so a photo dump taken six months ago lands on the
    /// timeline at its real date.
    /// </summary>
    private void TrackEarliestCapture(string filePath, ref DateTime? earliest)
    {
        var captured = _dateExtractor.TryExtract(filePath);
        if (captured is null) return;
        if (earliest is null || captured.Value < earliest.Value)
            earliest = captured.Value;
    }

    private OperationResult Validate(VideoUploadRequest request)
    {
        if (request.Files.Count == 0)
            return OperationResult.Failure("No files were selected.");
        return ValidateFiles(request.Files);
    }

    private OperationResult ValidateFiles(IReadOnlyList<IBrowserFile> files)
    {
        foreach (var file in files)
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

    private async Task<MediaItem> StoreImageAsync(
        IBrowserFile file, int videoId, int photoIndex, int order)
    {
        var targetName = $"photo-{photoIndex:D2}.jpg";
        var savedPath = await _storage.SaveImageWithResizeAsync(
            file, videoId, _options.MaxFileSizeBytes, targetName);
        var size = new FileInfo(savedPath).Length;

        // First photo appended to an item that did not yet have a real
        // photo becomes the new OG preview source. We always regenerate
        // og.jpg from the freshest first image — easier to reason about
        // than tracking which photo "owns" the preview.
        if (photoIndex == 1)
        {
            var ogPath = Path.Combine(
                _storage.GetVideoDirectoryPath(videoId), "og.jpg");
            await _storage.GenerateOgImageAsync(savedPath, ogPath);
        }

        _logger.LogInformation(
            "Item {Id}: appended image {Name} ({KB} KB)",
            videoId, targetName, size / 1024);
        return MediaItem.Create(targetName, MediaType.Image, size, order);
    }

    private async Task<(MediaItem Item, string? NewPrimary)> StoreVideoAsync(
        IBrowserFile file, int videoId, int videoIndex, int order, bool hasPrimary)
    {
        var compressedName = hasPrimary
            ? $"video-{videoIndex:D2}.mp4"
            : "video.mp4";

        var originalPath = await _storage.SaveUploadedFileAsync(
            file, videoId, _options.MaxFileSizeBytes,
            targetFileName: $"orig-{videoIndex:D2}{Path.GetExtension(file.Name)}");

        var compressedPath = Path.Combine(
            _storage.GetVideoDirectoryPath(videoId), compressedName);

        var (finalName, finalSize) = await CompressAndFinalizeAsync(
            originalPath, compressedPath, file.Size, videoId);

        _logger.LogInformation(
            "Item {Id}: appended video {Name} ({MB} MB)",
            videoId, finalName, finalSize / (1024 * 1024));

        return (
            MediaItem.Create(finalName, MediaType.Video, finalSize, order),
            hasPrimary ? null : finalName);
    }

    private async Task<(string FileName, long Size)> CompressAndFinalizeAsync(
        string originalPath, string compressedPath, long originalSize, int videoId)
    {
        _logger.LogInformation("Item {Id}: starting compression with [{Strategy}]",
            videoId, _compression.Name);

        var crf = _options.CompressionCrf;
        var produced = false;
        long compressedSize = 0;

        // Adaptive retry: encode at the configured CRF first; if the
        // result is still over the target size budget, recompress with a
        // higher CRF (more aggressive) until it fits or we hit the cap.
        while (true)
        {
            produced = await _compression.CompressAsync(
                originalPath, compressedPath, crfOverride: crf);
            if (!produced || !File.Exists(compressedPath)) break;

            compressedSize = new FileInfo(compressedPath).Length;
            if (compressedSize <= _options.TargetMaxOutputBytes) break;

            if (crf >= _options.MaxAdaptiveCrf)
            {
                _logger.LogInformation(
                    "Item {Id}: hit CRF cap {Cap} - keeping {MB} MB output",
                    videoId, _options.MaxAdaptiveCrf, compressedSize / (1024 * 1024));
                break;
            }

            crf = Math.Min(_options.MaxAdaptiveCrf, crf + _options.AdaptiveCrfStep);
            _logger.LogInformation(
                "Item {Id}: {MB} MB exceeds {TargetMB} MB budget - retrying at CRF {Crf}",
                videoId,
                compressedSize / (1024 * 1024),
                _options.TargetMaxOutputBytes / (1024 * 1024),
                crf);
        }

        if (produced && File.Exists(compressedPath))
        {
            try { File.Delete(originalPath); } catch { /* keep original on failure */ }

            _logger.LogInformation(
                "Item {Id}: {Old} MB → {New} MB at CRF {Crf}",
                videoId,
                originalSize / (1024 * 1024),
                compressedSize / (1024 * 1024),
                crf);

            return (Path.GetFileName(compressedPath), compressedSize);
        }

        _logger.LogWarning("Item {Id}: compression failed, keeping original", videoId);
        return (Path.GetFileName(originalPath), originalSize);
    }
}
