namespace MyHomePage.Models;

/// <summary>
/// Identifies the third-party platform a training session was sourced from.
/// Stored alongside the external id so multiple providers can coexist in
/// the same gallery without colliding (open/closed extension point).
/// </summary>
public enum TrainingSource
{
    /// <summary>Unknown / not provided.</summary>
    None = 0,

    /// <summary>Imported from Strava via OAuth + webhook.</summary>
    Strava = 1,

    /// <summary>Imported directly from Garmin Connect (reserved for future use).</summary>
    Garmin = 2,
}

/// <summary>
/// Training metrics attached to a gallery item — the numeric counterpart of
/// the photos/videos a user already uploaded. Optional everywhere: a gallery
/// item without an associated session simply leaves <c>Video.Training</c>
/// null. Values use SI units and ISO timestamps so consumers do not need to
/// know about each provider's quirks.
/// </summary>
public sealed record TrainingData
{
    /// <summary>Origin platform for traceability and merge/skip logic.</summary>
    public TrainingSource Source { get; init; } = TrainingSource.None;

    /// <summary>Provider-specific activity identifier (e.g. Strava id as a string).</summary>
    public string ExternalId { get; init; } = string.Empty;

    /// <summary>Provider-specific raw activity type ("Run", "WeightTraining"…).</summary>
    public string ActivityType { get; init; } = string.Empty;

    /// <summary>UTC start time of the session.</summary>
    public DateTime StartTimeUtc { get; init; }

    /// <summary>Moving time of the session.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>Distance covered in meters, if recorded.</summary>
    public double? DistanceMeters { get; init; }

    /// <summary>Average pace expressed as seconds per kilometer, if computable.</summary>
    public double? AveragePaceSecondsPerKm { get; init; }

    /// <summary>Total elevation gain in meters, if recorded.</summary>
    public double? ElevationGainMeters { get; init; }

    /// <summary>Average heart rate (bpm) over the session, if recorded.</summary>
    public int? AverageHeartRate { get; init; }

    /// <summary>Maximum heart rate (bpm) observed during the session.</summary>
    public int? MaxHeartRate { get; init; }

    /// <summary>Estimated calories burned, if provided by the source.</summary>
    public int? Calories { get; init; }

    /// <summary>
    /// Encoded GPS polyline (Google polyline algorithm, precision 5) so the
    /// route can be rendered on a Leaflet map without re-fetching from the
    /// provider. Null for indoor sessions.
    /// </summary>
    public string? RoutePolyline { get; init; }

    /// <summary>Deep link back to the activity on the provider's website.</summary>
    public string? ExternalUrl { get; init; }
}
