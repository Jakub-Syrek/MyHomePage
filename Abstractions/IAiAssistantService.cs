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

    /// <summary>
    /// Asks the assistant to write a weekly training report based on a
    /// prebuilt context block (already aggregated stats plus the recent
    /// sessions in compact JSON). Returns <c>null</c> when the assistant
    /// is unavailable or the response cannot be parsed.
    /// </summary>
    /// <param name="context">
    /// Pre-formatted context that the caller has already trimmed to fit
    /// the model's window. The caller owns the format / privacy boundary.
    /// </param>
    /// <param name="cancellationToken">Token used to cancel the HTTP call.</param>
    /// <returns>Structured coach report or <c>null</c> on failure.</returns>
    Task<CoachReportPayload?> GenerateCoachReportAsync(
        string context,
        CancellationToken cancellationToken = default);

    /// <summary>True if the service is wired up and ready to call.</summary>
    bool IsEnabled { get; }
}

/// <summary>
/// Plain-data result returned by the coach-report tool call. Mapped into a
/// <see cref="CoachReport"/> by the orchestrator that owns the persistence
/// concerns (week id, generated-at timestamp, model name).
/// </summary>
public sealed record CoachReportPayload
{
    /// <summary>One-sentence headline summarising the week.</summary>
    public string Headline { get; init; } = string.Empty;

    /// <summary>Two- or three-paragraph plain-text narrative.</summary>
    public string Narrative { get; init; } = string.Empty;

    /// <summary>Bullet-point highlights worth a callout.</summary>
    public IReadOnlyList<string> Highlights { get; init; } = Array.Empty<string>();

    /// <summary>Bullet-point concerns / red flags.</summary>
    public IReadOnlyList<string> Concerns { get; init; } = Array.Empty<string>();

    /// <summary>Bullet-point focus areas for next week.</summary>
    public IReadOnlyList<string> NextWeekFocus { get; init; } = Array.Empty<string>();
}
