using MyHomePage.Models;

namespace MyHomePage.Abstractions;

/// <summary>
/// Aggregates <see cref="TrainingData"/> across every gallery item into the
/// flat <see cref="TrainingStats"/> shape consumed by the <c>/stats</c>
/// page and the upcoming coach summary.
///
/// Pure transformation — depends on no I/O — so the implementation can be
/// reused from a background coach job that already has the videos in
/// memory without re-querying the repository.
/// </summary>
public interface ITrainingStatsService
{
    /// <summary>
    /// Computes statistics across the given videos, filtered to the
    /// supplied time window. Videos without a <see cref="Video.Training"/>
    /// record are ignored.
    /// </summary>
    /// <param name="videos">Source gallery items.</param>
    /// <param name="fromUtc">Inclusive start of the period.</param>
    /// <param name="toUtc">Inclusive end of the period.</param>
    TrainingStats Compute(
        IReadOnlyList<Video> videos,
        DateTime fromUtc,
        DateTime toUtc);
}
