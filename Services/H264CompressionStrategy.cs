using Microsoft.Extensions.Options;
using MyHomePage.Abstractions;
using MyHomePage.Options;
using Xabe.FFmpeg;

namespace MyHomePage.Services;

/// <summary>
/// Compression strategy that uses the H.264 (libx264) codec via FFmpeg.
/// Demonstrates the Strategy pattern: swap to a different ICompressionStrategy
/// (e.g. H.265, AV1) without touching VideoService.
/// </summary>
public sealed class H264CompressionStrategy : ICompressionStrategy
{
    private readonly VideoStorageOptions _options;
    private readonly ILogger<H264CompressionStrategy> _logger;

    public H264CompressionStrategy(
        IOptions<VideoStorageOptions> options,
        ILogger<H264CompressionStrategy> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public string Name => "H.264 / libx264";

    public async Task<bool> CompressAsync(
        string inputPath,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (File.Exists(outputPath))
            {
                try { File.Delete(outputPath); } catch { /* ignore */ }
            }

            // Scale to max resolution while preserving aspect ratio; cap frame rate
            var scaleFilter =
                $"scale='min({_options.MaxWidthPixels},iw)':'min({_options.MaxHeightPixels},ih)'" +
                ":force_original_aspect_ratio=decrease:force_divisible_by=2" +
                $",fps={_options.MaxFrameRate}";

            var conversion = FFmpeg.Conversions.New()
                .AddParameter($"-i \"{inputPath}\"", ParameterPosition.PreInput)
                .AddParameter($"-vf \"{scaleFilter}\"")
                .AddParameter("-c:v libx264")
                .AddParameter($"-crf {_options.CompressionCrf}")
                .AddParameter("-preset medium")
                .AddParameter($"-maxrate {_options.MaxBitrateKbps}k")
                .AddParameter($"-bufsize {_options.MaxBitrateKbps * 2}k")
                .AddParameter("-profile:v main")
                .AddParameter("-level 4.0")
                .AddParameter("-c:a aac")
                .AddParameter($"-b:a {_options.AudioBitrateKbps}k")
                .AddParameter("-ac 2")
                .AddParameter("-movflags +faststart")
                .AddParameter("-pix_fmt yuv420p")
                .SetOutput(outputPath);

            await conversion.Start();

            return File.Exists(outputPath) && new FileInfo(outputPath).Length > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{Strategy}] Compression failed for '{Input}'", Name, inputPath);
            return false;
        }
    }
}
