using MyHomePage.Models;

namespace MyHomePage.Abstractions;

/// <summary>
/// Generates and persists <see cref="CoachReport"/> documents. Orchestrates
/// stats aggregation, AI generation and storage so callers (the UI page,
/// a future scheduled job) only need a single call.
/// </summary>
public interface ICoachReportService
{
    /// <summary>
    /// Generates the coach report for the calendar week the given date
    /// belongs to (Monday-anchored), persists it and returns the result.
    /// Existing reports for the same week are overwritten — call this
    /// only when the operator explicitly wants a fresh run.
    /// </summary>
    /// <param name="anyDateInTheWeek">Any UTC date inside the target week.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    Task<OperationResult<CoachReport>> GenerateForWeekAsync(
        DateTime anyDateInTheWeek,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns every persisted report, newest first.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel I/O.</param>
    Task<IReadOnlyList<CoachReport>> ListAsync(CancellationToken cancellationToken = default);
}
