namespace MyHomePage.Models;

/// <summary>
/// Aggregate statistics computed across every gallery item that carries
/// <see cref="TrainingData"/>. The shape is deliberately flat so the
/// <c>/stats</c> page can render each section without further reshaping.
/// </summary>
public sealed record TrainingStats
{
    /// <summary>Inclusive lower bound of the period that produced these stats.</summary>
    public DateTime FromUtc { get; init; }

    /// <summary>Inclusive upper bound of the period that produced these stats.</summary>
    public DateTime ToUtc { get; init; }

    /// <summary>Total number of sessions in the period.</summary>
    public int SessionCount { get; init; }

    /// <summary>Total distance covered, in metres.</summary>
    public double TotalDistanceMeters { get; init; }

    /// <summary>Total moving time across the sessions.</summary>
    public TimeSpan TotalDuration { get; init; }

    /// <summary>Sum of elevation gain across the sessions, in metres.</summary>
    public double TotalElevationGainMeters { get; init; }

    /// <summary>Sum of calories burned, when reported.</summary>
    public int TotalCalories { get; init; }

    /// <summary>Sum of Strava suffer scores, when reported.</summary>
    public int TotalSufferScore { get; init; }

    /// <summary>Total personal records achieved during the period.</summary>
    public int TotalPersonalRecords { get; init; }

    /// <summary>Total Strava achievements unlocked during the period.</summary>
    public int TotalAchievements { get; init; }

    /// <summary>Sessions per category (Mountains, Running, …).</summary>
    public IReadOnlyDictionary<string, int> SessionsByCategory { get; init; } =
        new Dictionary<string, int>();

    /// <summary>Distance per category, in metres.</summary>
    public IReadOnlyDictionary<string, double> DistanceByCategory { get; init; } =
        new Dictionary<string, double>();

    /// <summary>Weekly breakdown of distance + duration, oldest first.</summary>
    public IReadOnlyList<WeeklyTrainingBucket> WeeklyBuckets { get; init; } =
        Array.Empty<WeeklyTrainingBucket>();

    /// <summary>
    /// Top best-effort PRs across the whole period (course-best segments
    /// where <c>PersonalRecordRank == 1</c>). Capped to the most recent 10.
    /// </summary>
    public IReadOnlyList<TrainingBestEffortHighlight> RecentRecords { get; init; } =
        Array.Empty<TrainingBestEffortHighlight>();

    /// <summary>Longest streak of consecutive days with at least one session.</summary>
    public int LongestStreakDays { get; init; }

    /// <summary>Streak of consecutive days ending today (0 if today is a rest day).</summary>
    public int CurrentStreakDays { get; init; }
}

/// <summary>One row of the weekly mileage chart on the stats page.</summary>
public sealed record WeeklyTrainingBucket
{
    /// <summary>UTC date of the Monday that opens this ISO week.</summary>
    public DateTime WeekStartUtc { get; init; }

    /// <summary>ISO week label, e.g. "2026-W18".</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>Total distance for the week, in metres.</summary>
    public double DistanceMeters { get; init; }

    /// <summary>Total moving time for the week.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>Total elevation gain for the week, in metres.</summary>
    public double ElevationGainMeters { get; init; }

    /// <summary>Number of sessions in the week.</summary>
    public int SessionCount { get; init; }
}

/// <summary>One entry in the <see cref="TrainingStats.RecentRecords"/> table.</summary>
public sealed record TrainingBestEffortHighlight
{
    /// <summary>Distance label as reported by the source ("1k", "5k", …).</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Moving time on this effort.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>UTC start time of the session that produced the effort.</summary>
    public DateTime AchievedAtUtc { get; init; }

    /// <summary>Identifier of the gallery item the effort belongs to (deep link target).</summary>
    public int VideoId { get; init; }

    /// <summary>Gallery item title (for human display).</summary>
    public string SessionTitle { get; init; } = string.Empty;
}
