using Microsoft.AspNetCore.Components.Forms;

namespace MyHomePage.Abstractions;

/// <summary>
/// Abstraction for file system operations related to media storage.
/// Follows the Interface Segregation Principle (I in SOLID).
/// </summary>
public interface IFileStorageService
{
    string GetVideosRootPath();
    string GetVideoDirectoryPath(int id);
    string GetMetadataFilePath(int id);

    void EnsureVideoDirectoryExists(int id);
    bool VideoDirectoryExists(int id);

    /// <summary>
    /// Streams the browser file to disk in the item's directory. Uses
    /// <paramref name="targetFileName"/> if provided, otherwise <c>original{ext}</c>.
    /// Returns the saved file path.
    /// </summary>
    Task<string> SaveUploadedFileAsync(
        IBrowserFile file,
        int videoId,
        long maxFileSizeBytes,
        string? targetFileName = null);

    /// <summary>
    /// Saves an image with optional resize to <paramref name="maxDimension"/> pixels
    /// (preserving aspect ratio) and JPEG re-encoding at quality 85. Returns the
    /// final file path.
    /// </summary>
    Task<string> SaveImageWithResizeAsync(
        IBrowserFile file,
        int videoId,
        long maxFileSizeBytes,
        string targetFileName,
        int maxDimension = 2560,
        int jpegQuality = 85);

    Task DeleteVideoDirectoryAsync(int id);
}
