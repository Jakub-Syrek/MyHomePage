namespace MyHomePage.Models;

/// <summary>
/// Kind of media stored alongside a gallery item.
/// </summary>
public enum MediaType
{
    Video = 0,
    Image = 1
}

/// <summary>
/// Represents a single media file (video or image) belonging to a gallery item.
/// A gallery item can have many of these (e.g. multiple photos under one description).
/// </summary>
public sealed class MediaItem
{
    /// <summary>
    /// Cached set of image extensions. <see cref="HashSet{T}"/> with
    /// ordinal-ignore-case comparer is O(1) and avoids the per-call
    /// allocation that <c>ToLowerInvariant</c> would produce in
    /// <see cref="DetectType"/>.
    /// </summary>
    private static readonly HashSet<string> ImageExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".webp", ".heic", ".gif"
        };

    public string FileName { get; set; } = "";
    public MediaType Type { get; set; }
    public long SizeBytes { get; set; }

    /// <summary>Order index inside the gallery item (0 = primary).</summary>
    public int Order { get; set; }

    public static MediaItem Create(string fileName, MediaType type, long sizeBytes, int order = 0) => new()
    {
        FileName = fileName,
        Type = type,
        SizeBytes = sizeBytes,
        Order = order
    };

    /// <summary>
    /// Maps a file name to its <see cref="MediaType"/>. Unknown / unrecognised
    /// extensions default to <see cref="MediaType.Video"/> so legacy callers
    /// that only handled videos continue to round-trip safely.
    /// </summary>
    public static MediaType DetectType(string fileName) =>
        ImageExtensions.Contains(Path.GetExtension(fileName))
            ? MediaType.Image
            : MediaType.Video;
}
