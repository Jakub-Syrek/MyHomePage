using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Options;
using MyHomePage.Abstractions;
using MyHomePage.Options;

namespace MyHomePage.Services;

/// <summary>
/// Handles all file-system operations for video storage.
/// Single Responsibility Principle (S in SOLID): this class owns only the "where/how files live" concern.
/// </summary>
public sealed class FileStorageService : IFileStorageService
{
    private readonly IWebHostEnvironment _environment;
    private readonly VideoStorageOptions _options;

    public FileStorageService(IWebHostEnvironment environment, IOptions<VideoStorageOptions> options)
    {
        _environment = environment;
        _options = options.Value;
    }

    public string GetVideosRootPath() =>
        Path.Combine(_environment.WebRootPath, _options.VideosFolder);

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
