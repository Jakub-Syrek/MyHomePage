using MyHomePage.Models;

namespace MyHomePage.Abstractions;

public interface ILogReaderService
{
    /// <summary>Returns the most recent log entries, newest first.</summary>
    Task<IReadOnlyList<LogEntry>> GetEntriesAsync(int maxEntries = 500);

    /// <summary>Deletes all current log files.</summary>
    Task ClearAsync();

    /// <summary>Returns the path of the logs directory for display.</summary>
    string LogsDirectory { get; }
}
