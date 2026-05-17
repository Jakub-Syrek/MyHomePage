using MyHomePage.Models;

namespace MyHomePage.Abstractions;

/// <summary>
/// Persistence boundary for <see cref="CoachReport"/> instances. Reports
/// are stored under the same volume as videos so they survive container
/// restarts on Railway and stay grouped with the gallery data.
/// </summary>
public interface ICoachReportRepository
{
    /// <summary>
    /// Returns every persisted report, newest week first.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel I/O.</param>
    Task<IReadOnlyList<CoachReport>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the report for the given ISO week identifier.
    /// </summary>
    /// <param name="isoWeek">ISO week id (e.g. <c>2026-W20</c>).</param>
    /// <param name="cancellationToken">Token used to cancel I/O.</param>
    Task<CoachReport?> GetAsync(string isoWeek, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists a report, overwriting any previous version for the same week.
    /// </summary>
    /// <param name="report">Report to save.</param>
    /// <param name="cancellationToken">Token used to cancel I/O.</param>
    Task SaveAsync(CoachReport report, CancellationToken cancellationToken = default);
}
