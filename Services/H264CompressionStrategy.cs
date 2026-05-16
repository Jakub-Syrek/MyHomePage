using Microsoft.Extensions.Options;
using MyHomePage.Abstractions;
using MyHomePage.Options;
using Xabe.FFmpeg;

namespace MyHomePage.Services;

/// <summary>
/// Compression strategy that uses the H.264 (libx264) codec via FFmpeg.
/// Demonstrates the Strategy pattern: swap to a different ICompressionStrategy
/// (e.g. H.265, AV1) without touching VideoService.
///
/// Tuned for: high quality (CRF 23), Full HD (1920x1080), smooth web playback
/// (faststart + keyframe every 2s for instant seeking), and progressive download.
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
        int? crfOverride = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (File.Exists(outputPath))
            {
                try { File.Delete(outputPath); } catch { /* ignore */ }
            }

            // Scale to max resolution while preserving aspect ratio; cap frame rate.
            // force_divisible_by=2 ensures dimensions are even (H.264 requirement).
            var scaleFilter =
                $"scale='min({_options.MaxWidthPixels},iw)':'min({_options.MaxHeightPixels},ih)'" +
                ":force_original_aspect_ratio=decrease:force_divisible_by=2" +
                $",fps={_options.MaxFrameRate}";

            // GOP (keyframe interval) = fps * keyframe_seconds — enables fast seeking in browser
            var gop = _options.MaxFrameRate * _options.KeyframeIntervalSeconds;

            var crf = crfOverride ?? _options.CompressionCrf;

            var conversion = FFmpeg.Conversions.New()
                .AddParameter($"-i \"{inputPath}\"", ParameterPosition.PreInput)
                .AddParameter($"-vf \"{scaleFilter}\"")
                // Video codec + quality
                .AddParameter("-c:v libx264")
                .AddParameter($"-crf {crf}")
                .AddParameter($"-preset {_options.Preset}")
                .AddParameter($"-tune {_options.Tune}")
                // Bitrate cap (prevents quality spikes from exploding file size)
                .AddParameter($"-maxrate {_options.MaxBitrateKbps}k")
                .AddParameter($"-bufsize {_options.MaxBitrateKbps * 2}k")
                // H.264 profile/level — high profile for better quality, level 4.1 for 1080p30 compatibility
                .AddParameter("-profile:v high")
                .AddParameter("-level 4.1")
                // Keyframe interval — enables smooth seeking in browser players
                .AddParameter($"-g {gop}")
                .AddParameter($"-keyint_min {gop}")
                .AddParameter("-sc_threshold 0")
                // Audio: AAC stereo, web-quality
                .AddParameter("-c:a aac")
                .AddParameter($"-b:a {_options.AudioBitrateKbps}k")
                .AddParameter("-ac 2")
                .AddParameter("-ar 48000")
                // Web optimization — moves moov atom to start of file → progressive playback
                .AddParameter("-movflags +faststart")
                // 4:2:0 chroma — universal browser/device compatibility
                .AddParameter("-pix_fmt yuv420p")
                .SetOutput(outputPath);

            _logger.LogInformation(
                "[{Strategy}] Encoding: CRF={Crf} preset={Preset} tune={Tune} maxres={W}x{H} gop={Gop}f",
                Name, crf, _options.Preset, _options.Tune,
                _options.MaxWidthPixels, _options.MaxHeightPixels, gop);

            await conversion.Start(cancellationToken);

            return File.Exists(outputPath) && new FileInfo(outputPath).Length > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{Strategy}] Compression failed for '{Input}'", Name, inputPath);
            return false;
        }
    }
}
