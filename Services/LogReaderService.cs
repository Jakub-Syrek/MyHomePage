using System.Text.Json;
using MyHomePage.Abstractions;
using MyHomePage.Models;

namespace MyHomePage.Services;

/// <summary>
/// Reads Serilog CLEF log files from the logs directory and exposes them
/// to the admin log-viewer page.
/// </summary>
public sealed class LogReaderService : ILogReaderService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public string LogsDirectory { get; }

    public LogReaderService(IWebHostEnvironment environment)
    {
        LogsDirectory = Path.Combine(environment.ContentRootPath, "logs");
    }

    public async Task<IReadOnlyList<LogEntry>> GetEntriesAsync(int maxEntries = 500)
    {
        if (!Directory.Exists(LogsDirectory))
            return [];

        // Read all .clef files, newest file first
        var files = Directory.GetFiles(LogsDirectory, "*.clef")
                             .OrderByDescending(f => File.GetLastWriteTime(f))
                             .ToList();

        var entries = new List<LogEntry>(maxEntries);

        foreach (var file in files)
        {
            if (entries.Count >= maxEntries) break;

            var fileEntries = await ReadFileAsync(file);
            // Newest entries first within each file
            fileEntries.Reverse();
            entries.AddRange(fileEntries.Take(maxEntries - entries.Count));
        }

        return entries.AsReadOnly();
    }

    public async Task ClearAsync()
    {
        if (!Directory.Exists(LogsDirectory)) return;

        foreach (var file in Directory.GetFiles(LogsDirectory, "*.clef"))
        {
            try
            {
                // Truncate rather than delete so Serilog file handle stays valid
                await File.WriteAllTextAsync(file, "");
            }
            catch
            {
                // File may be locked; ignore
            }
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static async Task<List<LogEntry>> ReadFileAsync(string path)
    {
        var entries = new List<LogEntry>();

        try
        {
            // Use ReadWrite share so we can read while Serilog still has the file open
            await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fs);

            while (await reader.ReadLineAsync() is { } line)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var entry = JsonSerializer.Deserialize<LogEntry>(line, JsonOpts);
                    if (entry != null) entries.Add(entry);
                }
                catch
                {
                    // Skip malformed lines
                }
            }
        }
        catch
        {
            // File inaccessible — return what we have
        }

        return entries;
    }
}
