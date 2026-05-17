using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using MyHomePage.Abstractions;
using MyHomePage.Models;

namespace MyHomePage.Services;

/// <summary>
/// File-system backed <see cref="ICoachReportRepository"/>. Reports live in
/// <c>{videoStorageRoot}/coach-reports/{isoWeek}.json</c> so they sit on the
/// same Railway volume as gallery items and Strava tokens.
/// </summary>
public sealed class JsonCoachReportRepository : ICoachReportRepository
{
    private const string FolderName = "coach-reports";

    private static readonly Regex IsoWeekPattern =
        new("^\\d{4}-W\\d{2}$", RegexOptions.Compiled);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly SemaphoreSlim FileLock = new(1, 1);

    private readonly IFileStorageService _storage;
    private readonly ILogger<JsonCoachReportRepository> _logger;

    /// <summary>
    /// Constructs a new repository on top of the supplied storage service.
    /// </summary>
    /// <param name="storage">Resolves the absolute storage root path.</param>
    /// <param name="logger">Structured logger for diagnostic events.</param>
    public JsonCoachReportRepository(
        IFileStorageService storage,
        ILogger<JsonCoachReportRepository> logger)
    {
        _storage = storage;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CoachReport>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        var directory = ResolveDirectory();
        if (!Directory.Exists(directory)) return Array.Empty<CoachReport>();

        var reports = new List<CoachReport>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var report = await TryReadAsync(path, cancellationToken);
            if (report is not null) reports.Add(report);
        }
        return reports
            .OrderByDescending(r => r.WeekStartUtc)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<CoachReport?> GetAsync(
        string isoWeek,
        CancellationToken cancellationToken = default)
    {
        if (!IsoWeekPattern.IsMatch(isoWeek)) return null;
        var path = Path.Combine(ResolveDirectory(), $"{isoWeek}.json");
        return await TryReadAsync(path, cancellationToken);
    }

    /// <inheritdoc />
    public async Task SaveAsync(
        CoachReport report,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (!IsoWeekPattern.IsMatch(report.IsoWeek))
            throw new ArgumentException(
                $"ISO week id '{report.IsoWeek}' does not match the expected YYYY-Www format.",
                nameof(report));

        var directory = ResolveDirectory();
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{report.IsoWeek}.json");

        await FileLock.WaitAsync(cancellationToken);
        try
        {
            await using var stream = File.Create(path);
            await JsonSerializer.SerializeAsync(stream, report, JsonOptions, cancellationToken);
            _logger.LogInformation(
                "Coach report persisted for week {Week} ({Bytes} bytes)",
                report.IsoWeek, new FileInfo(path).Length);
        }
        finally
        {
            FileLock.Release();
        }
    }

    private async Task<CoachReport?> TryReadAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return null;
        await FileLock.WaitAsync(cancellationToken);
        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<CoachReport>(
                stream, JsonOptions, cancellationToken);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Corrupt coach report at {Path} — skipping", path);
            return null;
        }
        finally
        {
            FileLock.Release();
        }
    }

    private string ResolveDirectory() =>
        Path.Combine(_storage.GetVideosRootPath(), FolderName);
}
