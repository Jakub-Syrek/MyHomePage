namespace MyHomePage.Models;

/// <summary>
/// A gallery item (post). Originally was just a single video — now supports
/// multiple videos and photos under a single title/description, plus optional
/// GPS coordinates extracted from EXIF/MP4 metadata so it can be plotted on the map.
///
/// Backward compatibility: `FileName` remains the primary media file. New items
/// also populate `Media` (which includes the primary as Order=0). Old items
/// (only FileName, empty Media) are normalised when read.
/// </summary>
public class Video
{
    public int Id { get; set; }
    public required string Title { get; set; }
    public required string Description { get; set; }

    /// <summary>Primary media file name (kept for backward compat).</summary>
    public required string FileName { get; set; }

    /// <summary>Free-text human-readable location.</summary>
    public string? Location { get; set; }

    public string Category { get; set; } = "";
    public DateTime UploadedAt { get; set; }
    public long FileSizeBytes { get; set; }

    // ── NEW (May 2026) ────────────────────────────────────────────────────────

    /// <summary>
    /// All media files attached to this gallery item, in order. The primary
    /// (FileName) is also represented here with Order = 0. Empty list = legacy
    /// item with only FileName.
    /// </summary>
    public List<MediaItem> Media { get; set; } = new();

    /// <summary>Latitude in decimal degrees (WGS84), null if unknown.</summary>
    public double? Latitude { get; set; }

    /// <summary>Longitude in decimal degrees (WGS84), null if unknown.</summary>
    public double? Longitude { get; set; }

    /// <summary>True if this item has plottable GPS coordinates.</summary>
    public bool HasCoordinates => Latitude.HasValue && Longitude.HasValue;

    /// <summary>
    /// Returns Media if populated, otherwise a synthesised single-item list
    /// built from FileName (so legacy items still render uniformly).
    /// </summary>
    public IReadOnlyList<MediaItem> GetAllMedia() =>
        Media.Count > 0
            ? Media.OrderBy(m => m.Order).ToList()
            : new[] { MediaItem.Create(FileName, MediaItem.DetectType(FileName), FileSizeBytes, 0) };

    /// <summary>
    /// Factory method: centralises construction of new Video instances.
    /// Hides the initialisation ceremony from callers (Factory Method pattern).
    /// </summary>
    public static Video Create(
        int id, string title, string description, string fileName,
        string? location, string category, long fileSizeBytes,
        List<MediaItem>? media = null,
        double? latitude = null, double? longitude = null) => new()
    {
        Id = id,
        Title = title,
        Description = description,
        FileName = fileName,
        Location = location,
        Category = category,
        FileSizeBytes = fileSizeBytes,
        UploadedAt = DateTime.UtcNow,
        Media = media ?? new List<MediaItem>(),
        Latitude = latitude,
        Longitude = longitude
    };
}

/// <summary>
/// Static catalogue of valid category identifiers plus navigation helpers.
/// Adding a new category here is the only change needed — pages and URLs follow automatically
/// (Open/Closed Principle: extend without modifying existing logic).
/// </summary>
public static class VideoCategories
{
    public const string Gory = "Mountains";
    public const string WspinaczkaSkalowa = "Rock Climbing";
    public const string Bouldering = "Bouldering";
    public const string ProwadzieniHala = "Indoor Climbing";
    public const string Calisthenics = "Calisthenics";
    public const string Running = "Running";

    public static IReadOnlyList<string> All =>
        [Gory, WspinaczkaSkalowa, Bouldering, ProwadzieniHala, Calisthenics, Running];

    /// <summary>Returns the route URL for the given category slug.</summary>
    public static string GetUrl(string category) => category switch
    {
        Gory => "/gory",
        WspinaczkaSkalowa => "/wspinaczka-skalowa",
        Bouldering => "/bouldering",
        ProwadzieniHala => "/prowadzeni-hala",
        Calisthenics => "/calisthenics",
        Running => "/running",
        _ => "/"
    };
}
