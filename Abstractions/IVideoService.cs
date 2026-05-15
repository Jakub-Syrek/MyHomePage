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
}
