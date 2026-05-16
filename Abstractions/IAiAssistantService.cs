using MyHomePage.Models;

namespace MyHomePage.Abstractions;

/// <summary>
/// AI assistant for content-generation tasks (Claude API).
/// </summary>
public interface IAiAssistantService
{
    /// <summary>
    /// Suggest title, description and location for a media upload based on
    /// the gallery category and a few user-supplied keywords.
    /// Returns <c>null</c> if the assistant is unavailable (no API key,
    /// network error, or invalid response).
    /// </summary>
    Task<UploadSuggestion?> SuggestForUploadAsync(
        string category,
        string keywords,
        CancellationToken cancellationToken = default);

    /// <summary>True if the service is wired up and ready to call.</summary>
    bool IsEnabled { get; }
}
