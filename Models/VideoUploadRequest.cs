using Microsoft.AspNetCore.Components.Forms;

namespace MyHomePage.Models;

/// <summary>
/// Value object / DTO for uploading a gallery item with one or more media files
/// (videos and/or images) sharing a single title/description.
/// </summary>
public sealed record VideoUploadRequest(
    IReadOnlyList<IBrowserFile> Files,
    string Title,
    string Description,
    string? Location,
    string Category = "",
    double? Latitude = null,
    double? Longitude = null)
{
    /// <summary>Backward-compat constructor for callers that still pass a single file.</summary>
    public VideoUploadRequest(
        IBrowserFile file,
        string title,
        string description,
        string? location,
        string category = "")
        : this(new[] { file }, title, description, location, category)
    { }
}
