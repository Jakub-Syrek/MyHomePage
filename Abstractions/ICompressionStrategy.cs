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
    Task<bool> CompressAsync(string inputPath, string outputPath,
        CancellationToken cancellationToken = default);
}
