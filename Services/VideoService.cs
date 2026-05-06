using Microsoft.Extensions.Options;
using MyHomePage.Abstractions;
using MyHomePage.Models;
using MyHomePage.Options;

namespace MyHomePage.Services;

/// <summary>
/// Orchestrates video operations by coordinating the repository, file-storage,
/// and compression strategy.
/// Single Responsibility Principle (S in SOLID): business logic only — no raw I/O or codec knowledge.
/// Dependency Inversion Principle (D in SOLID): depends on IVideoRepository, IFileStorageService,
/// and ICompressionStrategy — not on any concrete class.
/// </summary>
public sealed class VideoService : IVideoService
{
    private readonly IVideoRepository _repository;
    private readonly IFileStorageService _storage;
    private readonly ICompressionStrategy _compression;
    private readonly VideoStorageOptions _options;
    private readonly ILogger<VideoService> _logger;

    public VideoService(
        IVideoRepository repository,
        IFileStorageService storage,
        ICompressionStrategy compression,
        IOptions<VideoStorageOptions> options,
        ILogger<VideoService> logger)
    {
        _repository = repository;
        _storage = storage;
        _compression = compression;
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

            var originalPath = await _storage.SaveUploadedFileAsync(
                request.File, videoId, _options.MaxFileSizeBytes);

            _logger.LogInformation("Video {Id}: original saved ({Size} MB)",
                videoId, request.File.Size / (1024 * 1024));

            var compressedPath = Path.Combine(
                _storage.GetVideoDirectoryPath(videoId), "video.mp4");

            var (finalFileName, finalSize) =
                await CompressAndFinalizeAsync(originalPath, compressedPath, request.File.Size, videoId);

            var video = Video.Create(
                videoId, request.Title, request.Description,
                finalFileName, request.Location, request.Category, finalSize);

            await _repository.SaveAsync(video);

            return OperationResult<int>.Success(videoId, "Video uploaded successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Upload failed for '{Title}'", request.Title);
            return OperationResult<int>.Failure($"Upload error: {ex.Message}");
        }
    }

    public async Task<OperationResult> UpdateVideoAsync(int id, string title, string description, string? location)
    {
        _logger.LogInformation("Updating video {Id}: title='{Title}', location='{Location}'",
            id, title, location);

        var video = await _repository.GetByIdAsync(id);
        if (video is null)
        {
            _logger.LogWarning("Update failed: Video {Id} not found", id);
            return OperationResult.Failure($"Video {id} not found.");
        }

        video.Title = title;
        video.Description = description;
        video.Location = location;

        try
        {
            await _repository.SaveAsync(video);
            _logger.LogInformation("Video {Id} updated successfully", id);
            return OperationResult.Success("Changes saved successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating video {Id}", id);
            return OperationResult.Failure($"Save error: {ex.Message}");
        }
    }

    public async Task<OperationResult> DeleteVideoAsync(int id)
    {
        _logger.LogInformation("Deleting video {Id}", id);

        try
        {
            var deleted = await _repository.DeleteAsync(id);
            if (deleted)
            {
                _logger.LogInformation("Video {Id} deleted successfully", id);
                return OperationResult.Success("Video deleted.");
            }

            _logger.LogWarning("Delete failed: Video {Id} not found", id);
            return OperationResult.Failure($"Video {id} not found.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting video {Id}", id);
            return OperationResult.Failure($"Delete error: {ex.Message}");
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private OperationResult Validate(VideoUploadRequest request)
    {
        if (request.File.Size > _options.MaxFileSizeBytes)
            return OperationResult.Failure(
                $"File too large. Maximum: {_options.MaxFileSizeBytes / (1024 * 1024)} MB");

        var ext = Path.GetExtension(request.File.Name).ToLowerInvariant();
        if (!_options.AllowedExtensions.Contains(ext))
            return OperationResult.Failure(
                $"Unsupported format. Allowed: {string.Join(", ", _options.AllowedExtensions)}");

        return OperationResult.Success();
    }

    private async Task<(string FileName, long Size)> CompressAndFinalizeAsync(
        string originalPath, string compressedPath, long originalSize, int videoId)
    {
        _logger.LogInformation("Video {Id}: starting compression with [{Strategy}]",
            videoId, _compression.Name);

        var compressed = await _compression.CompressAsync(originalPath, compressedPath);

        if (compressed && File.Exists(compressedPath))
        {
            var compressedSize = new FileInfo(compressedPath).Length;
            try { File.Delete(originalPath); } catch { /* keep original on failure */ }

            _logger.LogInformation("Video {Id}: {Old} MB → {New} MB ({Ratio}x reduction)",
                videoId,
                originalSize / (1024 * 1024),
                compressedSize / (1024 * 1024),
                compressedSize > 0 ? originalSize / compressedSize : 0);

            return ("video.mp4", compressedSize);
        }

        _logger.LogWarning("Video {Id}: compression failed, keeping original", videoId);
        return (Path.GetFileName(originalPath), originalSize);
    }
}
