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

    /// <summary>
    /// Copies a static asset shipped with the app (under <c>wwwroot/</c>) into
    /// the video directory of the given gallery item. Used to seed
    /// Strava-imported placeholders with a real cover image so they render
    /// like normal photo uploads instead of empty media tiles.
    /// </summary>
    /// <param name="wwwRootRelativePath">Path relative to <c>wwwroot</c> (e.g. <c>images/running-bg.jpg</c>).</param>
    /// <param name="videoId">Target gallery item id.</param>
    /// <param name="targetFileName">File name to write under that item's directory.</param>
    /// <returns>Byte size of the written copy, or 0 when the source did not exist.</returns>
    Task<long> CopyWwwRootFileToVideoAsync(
        string wwwRootRelativePath,
        int videoId,
        string targetFileName);
}
