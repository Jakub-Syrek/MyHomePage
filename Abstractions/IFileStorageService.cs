using Microsoft.AspNetCore.Components.Forms;

namespace MyHomePage.Abstractions;

/// <summary>
/// Abstraction for file system operations related to video storage.
/// Follows the Interface Segregation Principle (I in SOLID):
/// consumers that only need path helpers don't depend on upload/delete methods.
/// </summary>
public interface IFileStorageService
{
    string GetVideosRootPath();
    string GetVideoDirectoryPath(int id);
    string GetMetadataFilePath(int id);

    void EnsureVideoDirectoryExists(int id);
    bool VideoDirectoryExists(int id);

    /// <summary>Streams the browser file to disk and returns the saved file path.</summary>
    Task<string> SaveUploadedFileAsync(IBrowserFile file, int videoId, long maxFileSizeBytes);

    Task DeleteVideoDirectoryAsync(int id);
}
