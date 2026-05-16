using System.Text.Json;
using Microsoft.Extensions.Logging;
using MyHomePage.Abstractions;
using MyHomePage.Models;

namespace MyHomePage.Services;

/// <summary>
/// Persists the Strava OAuth token set as a single JSON file under the
/// video storage root. The storage root is already mounted on a Railway
/// volume in production, so tokens survive container restarts. Single-user
/// app — no multi-tenant concerns.
/// </summary>
public sealed class JsonStravaTokenStore : IStravaTokenStore
{
    private const string FileName = "strava-tokens.json";

    private static readonly JsonSerializerOptions JsonOptions =
        new() { WriteIndented = true };

    private static readonly SemaphoreSlim FileLock = new(1, 1);

    private readonly IFileStorageService _storage;
    private readonly ILogger<JsonStravaTokenStore> _logger;

    /// <summary>
    /// Creates a new token store backed by the supplied storage service.
    /// </summary>
    /// <param name="storage">Resolves the absolute path of the storage root.</param>
    /// <param name="logger">Structured logger for diagnostic events.</param>
    public JsonStravaTokenStore(
        IFileStorageService storage,
        ILogger<JsonStravaTokenStore> logger)
    {
        _storage = storage;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<StravaTokenSet?> LoadAsync(CancellationToken cancellationToken = default)
    {
        var path = ResolvePath();
        if (!File.Exists(path))
        {
            _logger.LogDebug("Strava token file not present at {Path}", path);
            return null;
        }

        await FileLock.WaitAsync(cancellationToken);
        try
        {
            await using var stream = File.OpenRead(path);
            var tokens = await JsonSerializer.DeserializeAsync<StravaTokenSet>(
                stream, JsonOptions, cancellationToken);
            return tokens;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Strava token file at {Path} is corrupt", path);
            return null;
        }
        finally
        {
            FileLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync(StravaTokenSet tokens, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tokens);

        var path = ResolvePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        await FileLock.WaitAsync(cancellationToken);
        try
        {
            await using var stream = File.Create(path);
            await JsonSerializer.SerializeAsync(stream, tokens, JsonOptions, cancellationToken);
            _logger.LogInformation(
                "Strava tokens persisted for athlete {AthleteId}, expires at {Expires}",
                tokens.AthleteId, tokens.ExpiresAtUtc);
        }
        finally
        {
            FileLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task DeleteAsync(CancellationToken cancellationToken = default)
    {
        var path = ResolvePath();
        await FileLock.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                _logger.LogInformation("Strava tokens removed from {Path}", path);
            }
        }
        finally
        {
            FileLock.Release();
        }
    }

    private string ResolvePath() =>
        Path.Combine(_storage.GetVideosRootPath(), FileName);
}
