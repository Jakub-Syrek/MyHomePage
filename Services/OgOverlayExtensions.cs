using MyHomePage.Abstractions;
using MyHomePage.Models;

namespace MyHomePage.Services;

/// <summary>
/// Helpers that turn a gallery <see cref="Video"/> into the
/// <see cref="OgOverlay"/> payload <see cref="IFileStorageService.GenerateOgImageAsync"/>
/// uses to render the Facebook / OG preview's stats strip. Co-located in
/// one place so every entry point (upload, append, Strava import, editor
/// re-crop) renders consistent overlays without each caller having to
/// remember which fields to map.
/// </summary>
public static class OgOverlayExtensions
{
    /// <summary>
    /// Builds an overlay payload from the supplied video. Returns
    /// <c>null</c> when nothing meaningful is available so the bottom
    /// strip is skipped instead of drawing an empty bar.
    /// </summary>
    /// <param name="video">Source gallery item.</param>
    /// <returns>The overlay payload, or <c>null</c>.</returns>
    public static OgOverlay? ToOgOverlay(this Video video)
    {
        ArgumentNullException.ThrowIfNull(video);

        var training = video.Training;
        var hasAnything =
            training is not null ||
            !string.IsNullOrWhiteSpace(video.Location) ||
            !string.IsNullOrWhiteSpace(video.Category);
        if (!hasAnything) return null;

        return new OgOverlay
        {
            ActivityLabel = !string.IsNullOrWhiteSpace(training?.ActivityType)
                ? training.ActivityType
                : video.Category,
            DistanceMeters = training?.DistanceMeters,
            Duration = training is not null && training.Duration > TimeSpan.Zero
                ? training.Duration
                : null,
            PaceSecondsPerKm = training?.AveragePaceSecondsPerKm,
            Calories = training?.Calories,
            ElevationGainMeters = training?.ElevationGainMeters,
            CapturedAt = training?.StartTimeUtc ?? video.UploadedAt,
            Location = video.Location
        };
    }
}
