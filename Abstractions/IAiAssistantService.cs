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

    /// <summary>
    /// Extracts a free-text location (gym, venue, town…) from the loose
    /// fields a third-party activity tracker exposes — typically an
    /// activity title and a description. Returns <c>null</c> when no
    /// confident venue can be identified or the assistant is unavailable.
    /// </summary>
    /// <param name="activityName">Activity title as entered by the athlete.</param>
    /// <param name="description">Free-form description body (may be empty).</param>
    /// <param name="activityType">Provider-specific activity type for context.</param>
    /// <param name="cancellationToken">Token used to cancel the HTTP call.</param>
    Task<string?> ExtractLocationAsync(
        string activityName,
        string description,
        string activityType,
        CancellationToken cancellationToken = default);

    /// <summary>True if the service is wired up and ready to call.</summary>
    bool IsEnabled { get; }
}
