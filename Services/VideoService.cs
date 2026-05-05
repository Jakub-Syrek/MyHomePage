using System.Text.Json;
using Microsoft.AspNetCore.Components.Forms;
using MyHomePage.Models;
using Xabe.FFmpeg;

namespace MyHomePage.Services;

public class VideoService(IWebHostEnvironment environment, ILogger<VideoService> logger)
{
    private readonly IWebHostEnvironment _environment = environment;
    private readonly ILogger<VideoService> _logger = logger;
    private const string VIDEOS_FOLDER = "videos";
    private const long MAX_FILE_SIZE = 1024L * 1024 * 1024 * 2; // 2 GB
    private static readonly string[] AllowedExtensions = [".mp4", ".webm", ".mkv", ".avi"];
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private string GetVideosPath() => Path.Combine(_environment.WebRootPath, VIDEOS_FOLDER);
    private string GetVideoPath(int id) => Path.Combine(GetVideosPath(), id.ToString());
    private string GetMetadataPath(int id) => Path.Combine(GetVideoPath(id), "metadata.json");

    public async Task<List<Video>> GetAllVideosAsync()
    {
        var videosPath = GetVideosPath();
        if (!Directory.Exists(videosPath)) return [];

        var videos = new List<Video>();
        var dirs = Directory.GetDirectories(videosPath);

        foreach (var dir in dirs.OrderByDescending(d => new DirectoryInfo(d).CreationTime))
        {
            var metadataPath = Path.Combine(dir, "metadata.json");
            if (File.Exists(metadataPath))
            {
                var json = await File.ReadAllTextAsync(metadataPath);
                var metadata = JsonSerializer.Deserialize<Video>(json);
                if (metadata != null)
                    videos.Add(metadata);
            }
        }

        return videos;
    }

    public async Task<List<Video>> GetVideosByCategoryAsync(string category)
    {
        var allVideos = await GetAllVideosAsync();
        return allVideos.Where(v => v.Category == category).ToList();
    }

    public async Task<Video?> GetVideoByIdAsync(int id)
    {
        var metadataPath = GetMetadataPath(id);
        if (!File.Exists(metadataPath)) return null;

        var json = await File.ReadAllTextAsync(metadataPath);
        return JsonSerializer.Deserialize<Video>(json);
    }

    public async Task<(bool Success, string Message, int? VideoId)> UploadVideoAsync(
        IBrowserFile file,
        string title,
        string description,
        string? location,
        string category = "")
    {
        if (file.Size > MAX_FILE_SIZE)
            return (false, $"File too large. Maximum: {MAX_FILE_SIZE / (1024 * 1024)} MB", null);

        var ext = Path.GetExtension(file.Name).ToLowerInvariant();
        if (!AllowedExtensions.Contains(ext))
            return (false, $"Unsupported format. Allowed: {string.Join(", ", AllowedExtensions)}", null);

        try
        {
            var videosPath = GetVideosPath();
            Directory.CreateDirectory(videosPath);

            var videoId = GenerateVideoId();
            var videoDir = GetVideoPath(videoId);
            Directory.CreateDirectory(videoDir);

            // Save uploaded file as original
            var originalFileName = $"original{ext}";
            var originalPath = Path.Combine(videoDir, originalFileName);

            await using (var stream = file.OpenReadStream(MAX_FILE_SIZE))
            await using (var fs = new FileStream(originalPath, FileMode.Create))
            {
                await stream.CopyToAsync(fs);
            }

            _logger.LogInformation("Saved original file: {Path} ({Size} MB)", originalPath, file.Size / (1024 * 1024));

            // Compress to video.mp4 (target: ~20x smaller)
            var compressedFileName = "video.mp4";
            var compressedPath = Path.Combine(videoDir, compressedFileName);

            string finalFileName = originalFileName;
            long finalSize = file.Size;

            bool compressed = await CompressVideoAsync(originalPath, compressedPath);
            if (compressed && File.Exists(compressedPath))
            {
                finalFileName = compressedFileName;
                finalSize = new FileInfo(compressedPath).Length;
                try { File.Delete(originalPath); } catch { /* keep original if delete fails */ }
                _logger.LogInformation("Compressed: {OldSize} MB -> {NewSize} MB ({Ratio}x)",
                    file.Size / (1024 * 1024), finalSize / (1024 * 1024),
                    finalSize > 0 ? file.Size / finalSize : 0);
            }
            else
            {
                _logger.LogWarning("Compression failed, keeping original file");
            }

            var video = new Video
            {
                Id = videoId,
                Title = title,
                Description = description,
                FileName = finalFileName,
                Location = location,
                Category = category,
                FileSizeBytes = finalSize,
                UploadedAt = DateTime.UtcNow
            };

            var metadataJson = JsonSerializer.Serialize(video, JsonOptions);
            await File.WriteAllTextAsync(GetMetadataPath(videoId), metadataJson);

            return (true, "Video uploaded successfully", videoId);
        }
        catch (Exception ex)
        {
            return (false, $"Upload error: {ex.Message}", null);
        }
    }

    private async Task<bool> CompressVideoAsync(string inputPath, string outputPath)
    {
        try
        {
            // Very aggressive compression for streaming over throttled connections (free ngrok)
            // - Scale to max 480p (854x480) - 4x less pixels than 720p
            // - 24 fps cap (halves the data rate vs 60fps source)
            // - CRF 36 for very strong compression
            // - Bitrate cap at 600 kbps video (smooth on ~1 Mbps connections)
            // - AAC 48k mono audio (sufficient for speech/ambient)
            // - +faststart puts metadata at file start so playback can begin immediately
            // Delete output if it already exists - Xabe.FFmpeg uses -n by default
            if (File.Exists(outputPath))
            {
                try { File.Delete(outputPath); } catch { /* ignore */ }
            }

            var conversion = FFmpeg.Conversions.New()
                .AddParameter($"-i \"{inputPath}\"", ParameterPosition.PreInput)
                .AddParameter("-vf \"scale='min(854,iw)':'min(480,ih)':force_original_aspect_ratio=decrease,fps=24\"")
                .AddParameter("-c:v libx264")
                .AddParameter("-crf 36")
                .AddParameter("-preset medium")
                .AddParameter("-maxrate 600k")
                .AddParameter("-bufsize 1200k")
                .AddParameter("-profile:v main")
                .AddParameter("-level 3.1")
                .AddParameter("-c:a aac")
                .AddParameter("-b:a 48k")
                .AddParameter("-ac 1")
                .AddParameter("-movflags +faststart")
                .AddParameter("-pix_fmt yuv420p")
                .SetOutput(outputPath);

            await conversion.Start();
            return File.Exists(outputPath) && new FileInfo(outputPath).Length > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FFmpeg compression failed for {Input}", inputPath);
            return false;
        }
    }

    public async Task<bool> UpdateVideoAsync(int id, string title, string description, string? location)
    {
        var video = await GetVideoByIdAsync(id);
        if (video == null) return false;

        video.Title = title;
        video.Description = description;
        video.Location = location;

        var metadataJson = JsonSerializer.Serialize(video, JsonOptions);
        await File.WriteAllTextAsync(GetMetadataPath(id), metadataJson);
        return true;
    }

    public async Task<bool> DeleteVideoAsync(int id)
    {
        var videoDir = GetVideoPath(id);
        if (!Directory.Exists(videoDir)) return false;

        try
        {
            Directory.Delete(videoDir, recursive: true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private int GenerateVideoId()
    {
        var videosPath = GetVideosPath();
        if (!Directory.Exists(videosPath))
            return 1;

        var dirs = Directory.GetDirectories(videosPath);
        return dirs.Length > 0
            ? dirs.Select(d => int.TryParse(Path.GetFileName(d), out var id) ? id : 0).Max() + 1
            : 1;
    }
}
