using MyHomePage.Abstractions;
using MyHomePage.Models;

namespace MyHomePage.Services;

/// <summary>
/// Transparent logging wrapper around any IVideoRepository implementation.
/// Demonstrates the Decorator pattern: adds behaviour (logging) without subclassing
/// and without modifying the wrapped class.
/// Open/Closed Principle (O in SOLID): the inner repository is closed for modification
/// while this decorator extends it openly.
/// </summary>
public sealed class LoggingVideoRepository : IVideoRepository
{
    private readonly IVideoRepository _inner;
    private readonly ILogger<LoggingVideoRepository> _logger;

    public LoggingVideoRepository(IVideoRepository inner, ILogger<LoggingVideoRepository> logger)
    {
        _inner = inner;
        _logger = logger;
    }

    public async Task<IReadOnlyList<Video>> GetAllAsync()
    {
        _logger.LogDebug("Repository → GetAllAsync");
        var result = await _inner.GetAllAsync();
        _logger.LogDebug("Repository ← {Count} videos returned", result.Count);
        return result;
    }

    public async Task<Video?> GetByIdAsync(int id)
    {
        _logger.LogDebug("Repository → GetByIdAsync({Id})", id);
        var result = await _inner.GetByIdAsync(id);
        _logger.LogDebug("Repository ← video {Id} {Status}", id, result != null ? "found" : "not found");
        return result;
    }

    public async Task SaveAsync(Video video)
    {
        _logger.LogInformation("Repository → SaveAsync(id={Id}, title='{Title}')", video.Id, video.Title);
        await _inner.SaveAsync(video);
        _logger.LogInformation("Repository ← SaveAsync done");
    }

    public async Task<bool> DeleteAsync(int id)
    {
        _logger.LogInformation("Repository → DeleteAsync({Id})", id);
        var deleted = await _inner.DeleteAsync(id);
        _logger.LogInformation("Repository ← DeleteAsync({Id}) = {Result}", id, deleted ? "deleted" : "not found");
        return deleted;
    }

    public int GenerateNextId() => _inner.GenerateNextId();
}
