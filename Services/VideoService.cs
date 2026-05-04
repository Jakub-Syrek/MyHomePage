using System.Text.Json;
using Microsoft.AspNetCore.Components.Forms;
using MyHomePage.Models;

namespace MyHomePage.Services;

public class VideoService(IWebHostEnvironment environment)
{
    private readonly IWebHostEnvironment _environment = environment;
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
        string? location)
    {
        if (file.Size > MAX_FILE_SIZE)
            return (false, $"Plik zbyt duży. Maksimum: {MAX_FILE_SIZE / (1024 * 1024)} MB", null);

        var ext = Path.GetExtension(file.Name).ToLowerInvariant();
        if (!AllowedExtensions.Contains(ext))
            return (false, $"Format nie obsługiwany. Dozwolone: {string.Join(", ", AllowedExtensions)}", null);

        try
        {
            var videosPath = GetVideosPath();
            Directory.CreateDirectory(videosPath);

            var videoId = GenerateVideoId();
            var videoDir = GetVideoPath(videoId);
            Directory.CreateDirectory(videoDir);

            var fileName = $"video{ext}";
            var filePath = Path.Combine(videoDir, fileName);

            await using var stream = file.OpenReadStream(MAX_FILE_SIZE);
            await using var fs = new FileStream(filePath, FileMode.Create);
            await stream.CopyToAsync(fs);

            var video = new Video
            {
                Id = videoId,
                Title = title,
                Description = description,
                FileName = fileName,
                Location = location,
                FileSizeBytes = file.Size,
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
