using MyHomePage.Models;

namespace MyHomePage.Abstractions;

/// <summary>
/// Application-level service interface for video operations.
/// UI components depend on this abstraction, not on a concrete service class
/// (Dependency Inversion Principle — D in SOLID).
/// </summary>
public interface IVideoService
{
    Task<IReadOnlyList<Video>> GetAllVideosAsync();
    Task<IReadOnlyList<Video>> GetVideosByCategoryAsync(string category);
    Task<Video?> GetVideoByIdAsync(int id);

    Task<OperationResult<int>> UploadVideoAsync(VideoUploadRequest request);
    Task<OperationResult> UpdateVideoAsync(int id, string title, string description, string? location);
    Task<OperationResult> UpdateVideoAsync(int id, string title, string description, string? location, double? latitude, double? longitude);
    Task<OperationResult> DeleteVideoAsync(int id);

    /// <summary>
    /// Appends additional photos / videos to an existing gallery item without
    /// touching its metadata. New files are compressed (videos) or resized
    /// (images) and added at the end of the item's media list. Useful when
    /// an item was initially created without media (e.g. a Strava import).
    /// </summary>
    /// <param name="videoId">Identifier of the gallery item to append to.</param>
    /// <param name="files">New files to append. Must satisfy the same size /
    /// extension rules as <see cref="UploadVideoAsync"/>.</param>
    Task<OperationResult> AppendMediaAsync(
        int videoId,
        IReadOnlyList<Microsoft.AspNetCore.Components.Forms.IBrowserFile> files);
}
