namespace MyHomePage.Models;

public class VideoMetadata
{
    public int Id { get; set; }
    public required string Title { get; set; }
    public required string Description { get; set; }
    public required string FileName { get; set; }
    public string? Location { get; set; }
    public DateTime UploadedAt { get; set; }
    public long FileSizeBytes { get; set; }
}
