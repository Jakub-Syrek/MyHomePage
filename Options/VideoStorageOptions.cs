namespace MyHomePage.Options;

/// <summary>
/// Strongly-typed configuration options for video storage and compression.
/// Follows the Options pattern (IOptions&lt;T&gt;) so settings can be changed
/// via appsettings.json without recompiling.
/// </summary>
public sealed class VideoStorageOptions
{
    public const string SectionName = "VideoStorage";

    public string VideosFolder { get; set; } = "videos";

    /// <summary>Maximum accepted upload size in bytes (default: 2 GB).</summary>
    public long MaxFileSizeBytes { get; set; } = 1024L * 1024 * 1024 * 2;

    public string[] AllowedExtensions { get; set; } = [".mp4", ".webm", ".mkv", ".avi"];

    // --- H.264 compression settings ---

    /// <summary>Constant Rate Factor: lower = better quality, larger file. Range 0–51.</summary>
    public int CompressionCrf { get; set; } = 30;

    public int MaxWidthPixels { get; set; } = 1280;
    public int MaxHeightPixels { get; set; } = 720;
    public int MaxFrameRate { get; set; } = 30;

    /// <summary>Peak video bitrate cap in kbps.</summary>
    public int MaxBitrateKbps { get; set; } = 2500;

    /// <summary>Audio bitrate in kbps.</summary>
    public int AudioBitrateKbps { get; set; } = 96;
}
