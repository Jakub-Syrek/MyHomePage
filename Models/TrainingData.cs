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

    // ── Extended metrics (Phase 1 — richer Strava import) ───────────────

    /// <summary>Maximum speed observed during the session, in m/s.</summary>
    public double? MaxSpeedMetersPerSecond { get; init; }

    /// <summary>Average cadence (steps/min for running, RPM for cycling).</summary>
    public double? AverageCadence { get; init; }

    /// <summary>Mean ambient temperature during the session, in °C.</summary>
    public double? AverageTempCelsius { get; init; }

    /// <summary>
    /// Strava "Relative Effort" / Suffer Score, 0–300. Higher means the
    /// session pushed further above the athlete's typical heart rate.
    /// </summary>
    public int? SufferScore { get; init; }

    /// <summary>Number of Strava achievements unlocked in this session.</summary>
    public int? AchievementCount { get; init; }

    /// <summary>Number of personal records set in this session.</summary>
    public int? PersonalRecordCount { get; init; }

    /// <summary>Kudos received on the activity (engagement proxy).</summary>
    public int? KudosCount { get; init; }

    /// <summary>True when Strava reports the activity was recorded on a trainer / indoor.</summary>
    public bool? IsTrainer { get; init; }

    /// <summary>True when Strava reports the activity was tagged as a commute.</summary>
    public bool? IsCommute { get; init; }

    /// <summary>True when the activity was entered manually (no GPS recording).</summary>
    public bool? IsManual { get; init; }

    /// <summary>Recording device name (e.g. "Garmin Forerunner 965").</summary>
    public string? DeviceName { get; init; }

    /// <summary>Gear used (typed as a label - shoe / bike name).</summary>
    public string? GearName { get; init; }

    /// <summary>Average power output in watts (cycling only).</summary>
    public double? AverageWatts { get; init; }

    /// <summary>Peak power output observed, in watts (cycling only).</summary>
    public double? MaxWatts { get; init; }

    /// <summary>Normalised power proxy used by Strava (weighted_average_watts).</summary>
    public double? WeightedAverageWatts { get; init; }

    /// <summary>Total work in kJ (cycling).</summary>
    public double? Kilojoules { get; init; }

    /// <summary>Per-kilometre split breakdown (running / hiking).</summary>
    public IReadOnlyList<TrainingSplit> Splits { get; init; } = Array.Empty<TrainingSplit>();

    /// <summary>Lap breakdown for activities the athlete manually lapped.</summary>
    public IReadOnlyList<TrainingLap> Laps { get; init; } = Array.Empty<TrainingLap>();

    /// <summary>Best-effort segments (e.g. 1k / 5k / 10k PRs hit during the session).</summary>
    public IReadOnlyList<TrainingBestEffort> BestEfforts { get; init; } = Array.Empty<TrainingBestEffort>();
}

/// <summary>
/// One row of a session's per-distance split breakdown (running / hiking).
/// All units SI.
/// </summary>
public sealed record TrainingSplit
{
    /// <summary>1-based split index within the activity.</summary>
    public int Index { get; init; }
    /// <summary>Length of this split in metres.</summary>
    public double DistanceMeters { get; init; }
    /// <summary>Moving time spent on this split.</summary>
    public TimeSpan Duration { get; init; }
    /// <summary>Pace in seconds per kilometre (computed from moving time + distance).</summary>
    public double? PaceSecondsPerKm { get; init; }
    /// <summary>Average heart rate during this split, when recorded.</summary>
    public int? AverageHeartRate { get; init; }
    /// <summary>Elevation gain or loss during this split, in metres.</summary>
    public double? ElevationChangeMeters { get; init; }
    /// <summary>Strava pace zone (1–4), when classified.</summary>
    public int? PaceZone { get; init; }
}

/// <summary>One manually-marked lap inside an activity.</summary>
public sealed record TrainingLap
{
    /// <summary>1-based lap index within the activity.</summary>
    public int Index { get; init; }
    /// <summary>Athlete-supplied or auto label.</summary>
    public string? Name { get; init; }
    /// <summary>Length of this lap in metres.</summary>
    public double DistanceMeters { get; init; }
    /// <summary>Moving time spent on this lap.</summary>
    public TimeSpan Duration { get; init; }
    /// <summary>Average heart rate over the lap, when recorded.</summary>
    public int? AverageHeartRate { get; init; }
    /// <summary>Peak heart rate over the lap, when recorded.</summary>
    public int? MaxHeartRate { get; init; }
    /// <summary>Average cadence over the lap, when recorded.</summary>
    public double? AverageCadence { get; init; }
    /// <summary>Elevation gain in metres over the lap.</summary>
    public double? ElevationGainMeters { get; init; }
}

/// <summary>One best-effort segment (1k / 5k / 10k / half-marathon time within an activity).</summary>
public sealed record TrainingBestEffort
{
    /// <summary>Distance label as Strava reports it ("1k", "5k", "10k"…).</summary>
    public string Name { get; init; } = string.Empty;
    /// <summary>Distance covered, in metres.</summary>
    public double DistanceMeters { get; init; }
    /// <summary>Moving time spent on this effort.</summary>
    public TimeSpan Duration { get; init; }
    /// <summary>1 = course best, 2 = second best, etc. Null when not a PR.</summary>
    public int? PersonalRecordRank { get; init; }
}
