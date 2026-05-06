using System.Text.Json;
using MyHomePage.Abstractions;
using MyHomePage.Models;

namespace MyHomePage.Services;

/// <summary>
/// Persists video metadata as JSON files on the local file system.
/// Repository pattern: encapsulates all data-access logic so higher layers
/// never need to know about JSON files or directory layout.
/// Single Responsibility Principle (S in SOLID): only concerned with reading/writing metadata.
/// </summary>
public sealed class JsonVideoRepository : IVideoRepository
{
    private readonly IFileStorageService _storage;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public JsonVideoRepository(IFileStorageService storage)
    {
        _storage = storage;
    }

    public async Task<IReadOnlyList<Video>> GetAllAsync()
    {
        var videosPath = _storage.GetVideosRootPath();
        if (!Directory.Exists(videosPath))
            return [];

        var videos = new List<Video>();
        var dirs = Directory.GetDirectories(videosPath)
                            .OrderByDescending(d => new DirectoryInfo(d).CreationTime);

        foreach (var dir in dirs)
        {
            var metadataPath = Path.Combine(dir, "metadata.json");
            if (!File.Exists(metadataPath)) continue;

            var json = await File.ReadAllTextAsync(metadataPath);
            var video = JsonSerializer.Deserialize<Video>(json);
            if (video != null) videos.Add(video);
        }

        return videos.AsReadOnly();
    }

    public async Task<Video?> GetByIdAsync(int id)
    {
        var path = _storage.GetMetadataFilePath(id);
        if (!File.Exists(path)) return null;

        var json = await File.ReadAllTextAsync(path);
        return JsonSerializer.Deserialize<Video>(json);
    }

    public async Task SaveAsync(Video video)
    {
        _storage.EnsureVideoDirectoryExists(video.Id);
        var json = JsonSerializer.Serialize(video, JsonOptions);
        await File.WriteAllTextAsync(_storage.GetMetadataFilePath(video.Id), json);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        if (!_storage.VideoDirectoryExists(id)) return false;
        await _storage.DeleteVideoDirectoryAsync(id);
        return true;
    }

    public int GenerateNextId()
    {
        var path = _storage.GetVideosRootPath();
        if (!Directory.Exists(path)) return 1;

        var dirs = Directory.GetDirectories(path);
        return dirs.Length == 0
            ? 1
            : dirs.Select(d => int.TryParse(Path.GetFileName(d), out var id) ? id : 0).Max() + 1;
    }
}
