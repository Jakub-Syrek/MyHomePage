namespace MyHomePage.Models;

public class Video
{
    public int Id { get; set; }
    public required string Title { get; set; }
    public required string Description { get; set; }
    public required string FileName { get; set; }
    public string? Location { get; set; }
    public string Category { get; set; } = "";
    public DateTime UploadedAt { get; set; }
    public long FileSizeBytes { get; set; }

    /// <summary>
    /// Factory method: centralises construction of new Video instances.
    /// Hides the initialisation ceremony from callers (Factory Method pattern).
    /// </summary>
    public static Video Create(
        int id, string title, string description, string fileName,
        string? location, string category, long fileSizeBytes) => new()
    {
        Id = id,
        Title = title,
        Description = description,
        FileName = fileName,
        Location = location,
        Category = category,
        FileSizeBytes = fileSizeBytes,
        UploadedAt = DateTime.UtcNow
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

    public static IReadOnlyList<string> All =>
        [Gory, WspinaczkaSkalowa, Bouldering, ProwadzieniHala];

    /// <summary>Returns the route URL for the given category slug.</summary>
    public static string GetUrl(string category) => category switch
    {
        Gory => "/gory",
        WspinaczkaSkalowa => "/wspinaczka-skalowa",
        Bouldering => "/bouldering",
        ProwadzieniHala => "/prowadzeni-hala",
        _ => "/"
    };
}
