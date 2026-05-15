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

    public static MediaType DetectType(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" or ".png" or ".webp" or ".heic" or ".gif" => MediaType.Image,
            _ => MediaType.Video
        };
    }
}
