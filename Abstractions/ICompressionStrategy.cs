namespace MyHomePage.Abstractions;

/// <summary>
/// Abstraction for a video compression algorithm.
/// Follows the Strategy pattern — different encoders can be swapped without touching VideoService.
/// Follows the Open/Closed Principle (O in SOLID): add new strategies without modifying existing code.
/// </summary>
public interface ICompressionStrategy
{
    /// <summary>Display name of this strategy (used in logging).</summary>
    string Name { get; }

    /// <summary>
    /// Compresses <paramref name="inputPath"/> and writes the result to <paramref name="outputPath"/>.
    /// Returns <c>true</c> when a valid output file is produced.
    /// </summary>
    /// <param name="inputPath">Source media file.</param>
    /// <param name="outputPath">Destination file (overwritten if present).</param>
    /// <param name="crfOverride">
    /// Optional Constant Rate Factor override. When null, the strategy
    /// uses its configured default. Adaptive retries pass a higher CRF
    /// to shrink the file when the previous attempt overshot the size
    /// budget.
    /// </param>
    /// <param name="cancellationToken">Token used to cancel the encode.</param>
    Task<bool> CompressAsync(string inputPath, string outputPath,
        int? crfOverride = null,
        CancellationToken cancellationToken = default);
}
