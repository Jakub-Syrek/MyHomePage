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
}

public static class VideoCategories
{
    public const string Gory = "Mountains";
    public const string WspinaczkaSkalowa = "Rock Climbing";
    public const string Bouldering = "Bouldering";
    public const string ProwadzieniHala = "Indoor Climbing";

    public static List<string> All => [Gory, WspinaczkaSkalowa, Bouldering, ProwadzieniHala];
}
