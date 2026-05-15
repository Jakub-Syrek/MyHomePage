namespace MyHomePage.Options;

/// <summary>
/// Strongly-typed configuration options for video storage and compression.
/// Follows the Options pattern (IOptions&lt;T&gt;) so settings can be changed
/// via appsettings.json without recompiling.
/// </summary>
public sealed class VideoStorageOptions
{
    public const string SectionName = "VideoStorage";

    /// <summary>
    /// Storage root path. If null, defaults to wwwroot/videos.
    /// Can be set via env var VIDEO_STORAGE_ROOT (e.g. /data/videos on Railway).
    /// </summary>
    public string? StorageRoot { get; set; }

    public string VideosFolder { get; set; } = "videos";

    /// <summary>Maximum accepted upload size in bytes (default: 5 GB).</summary>
    public long MaxFileSizeBytes { get; set; } = 1024L * 1024 * 1024 * 5;

    public string[] AllowedExtensions { get; set; } = [".mp4", ".webm", ".mkv", ".avi", ".mov", ".m4v"];

    // --- H.264 compression settings ---
    // Optimized for QUALITY + SMOOTH PLAYBACK while keeping file sizes reasonable.

    /// <summary>
    /// Constant Rate Factor: 18=visually lossless, 23=high quality (default), 28=acceptable, 51=worst.
    /// Sweet spot for web video: 22-24.
    /// </summary>
    public int CompressionCrf { get; set; } = 23;

    /// <summary>Target max width in pixels (1920 = Full HD).</summary>
    public int MaxWidthPixels { get; set; } = 1920;

    /// <summary>Target max height in pixels (1080 = Full HD).</summary>
    public int MaxHeightPixels { get; set; } = 1080;

    public int MaxFrameRate { get; set; } = 30;

    /// <summary>
    /// Peak video bitrate cap in kbps. 6000 = good for 1080p H.264.
    /// 1080p H.264 sweet spot: 5000-8000 kbps.
    /// </summary>
    public int MaxBitrateKbps { get; set; } = 6000;

    /// <summary>Audio bitrate in kbps (128 = web quality, 192 = high).</summary>
    public int AudioBitrateKbps { get; set; } = 160;

    /// <summary>
    /// x264 preset: ultrafast→placebo. Slower = better compression at same quality.
    /// "slow" gives 15-25% smaller files than "medium" at identical quality - great for Railway bandwidth.
    /// </summary>
    public string Preset { get; set; } = "slow";

    /// <summary>
    /// x264 tune: film (live-action), animation, grain, stillimage, fastdecode, zerolatency.
    /// "film" optimizes for live-action mountain/climbing footage.
    /// </summary>
    public string Tune { get; set; } = "film";

    /// <summary>
    /// Keyframe interval in seconds. Lower = better seeking, larger files.
    /// 2s is a good balance for smooth scrubbing in browser.
    /// </summary>
    public int KeyframeIntervalSeconds { get; set; } = 2;
}
