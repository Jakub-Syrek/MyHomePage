using MyHomePage.Models;

namespace MyHomePage.Abstractions;

/// <summary>
/// Merges two or more existing gallery items (collections) into a single
/// new gallery item. Media files are physically moved into the new
/// collection's directory, the source <see cref="Video"/> entries are
/// deleted, the per-activity <see cref="TrainingData"/> rows are rolled
/// up into a synthetic multi-sport summary, and a human-readable
/// <c>summary.md</c> is written alongside the media.
/// </summary>
public interface ICollectionMergeService
{
    /// <summary>
    /// Combines <paramref name="sourceIds"/> into a single new gallery item.
    /// </summary>
    /// <param name="sourceIds">Identifiers of the collections to merge (at least two).</param>
    /// <param name="title">Title for the merged collection.</param>
    /// <param name="description">Description for the merged collection. May be empty.</param>
    /// <param name="cancellationToken">Cancels the I/O.</param>
    /// <returns>
    /// On success, the identifier of the freshly created collection. On
    /// failure, a descriptive message — sources are left untouched whenever
    /// the merge cannot be completed.
    /// </returns>
    Task<OperationResult<int>> MergeAsync(
        IReadOnlyList<int> sourceIds,
        string title,
        string description,
        CancellationToken cancellationToken = default);
}
