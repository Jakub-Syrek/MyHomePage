namespace MyHomePage.Models;

/// <summary>
/// Suggested metadata for a media upload, returned by the AI assistant.
/// All fields are optional — the UI fills only those that came back populated.
/// </summary>
public sealed class UploadSuggestion
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string? Location { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
}
