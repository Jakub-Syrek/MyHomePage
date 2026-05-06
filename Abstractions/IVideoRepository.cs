using MyHomePage.Models;

namespace MyHomePage.Abstractions;

/// <summary>
/// Repository abstraction for video persistence.
/// Follows the Repository pattern and the Dependency Inversion Principle (D in SOLID).
/// </summary>
public interface IVideoRepository
{
    Task<IReadOnlyList<Video>> GetAllAsync();
    Task<Video?> GetByIdAsync(int id);
    Task SaveAsync(Video video);
    Task<bool> DeleteAsync(int id);
    int GenerateNextId();
}
