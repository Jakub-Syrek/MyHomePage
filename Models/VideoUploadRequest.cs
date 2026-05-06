using Microsoft.AspNetCore.Components.Forms;

namespace MyHomePage.Models;

/// <summary>
/// Value object / DTO that carries all data needed to upload a video.
/// Using a sealed record enforces immutability and makes the parameter list explicit —
/// replacing the raw parameter list on UploadVideoAsync.
/// </summary>
public sealed record VideoUploadRequest(
    IBrowserFile File,
    string Title,
    string Description,
    string? Location,
    string Category = "");
