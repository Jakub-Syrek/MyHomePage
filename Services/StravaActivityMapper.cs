using MyHomePage.Models;

namespace MyHomePage.Services;

/// <summary>
/// Pure mapping logic that translates Strava activity payloads into
/// our domain models. Kept stateless so it can be unit-tested with no
/// fixtures (Single Responsibility Principle).
/// </summary>
public static class StravaActivityMapper
{
    /// <summary>
    /// Determines which gallery category a Strava activity belongs to.
    /// </summary>
    /// <param name="activity">Activity returned by the Strava API.</param>
    /// <returns>
    /// One of the constants exposed on <see cref="VideoCategories"/>, or
    /// <see cref="VideoCategories.Running"/> as a generic cardio fallback.
    /// </returns>
    public static string ResolveCategory(StravaActivity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        var key = !string.IsNullOrWhiteSpace(activity.SportType)
            ? activity.SportType
            : activity.Type;
        return MapTypeToCategory(key);
    }

    /// <summary>
    /// Converts a Strava activity payload into the durable training record
    /// stored alongside a gallery item. Optionally accepts gear metadata so
    /// the caller can fetch <c>GET /api/v3/gear/{id}</c> when an activity
    /// references gear without inlining it in the activity body.
    /// </summary>
    /// <param name="activity">Activity returned by the Strava API.</param>
    /// <param name="gear">Resolved gear payload (or null when not fetched).</param>
    public static TrainingData ToTrainingData(StravaActivity activity, StravaGear? gear = null)
    {
        ArgumentNullException.ThrowIfNull(activity);
        var duration = TimeSpan.FromSeconds(activity.MovingTimeSeconds);
        return new TrainingData
        {
            Source = TrainingSource.Strava,
            ExternalId = activity.Id.ToString(),
            ActivityType = ResolveDisplayType(activity),
            StartTimeUtc = DateTime.SpecifyKind(activity.StartDate, DateTimeKind.Utc),
            Duration = duration,
            DistanceMeters = activity.DistanceMeters > 0 ? activity.DistanceMeters : null,
            AveragePaceSecondsPerKm = ComputePaceSecondsPerKm(activity.DistanceMeters, duration),
            ElevationGainMeters = activity.ElevationGainMeters,
            AverageHeartRate = ToNullableInt(activity.AverageHeartRate),
            MaxHeartRate = ToNullableInt(activity.MaxHeartRate),
            Calories = ToNullableInt(activity.Calories),
            RoutePolyline = activity.Map?.SummaryPolyline ?? activity.Map?.Polyline,
            ExternalUrl = $"https://www.strava.com/activities/{activity.Id}",

            // Extended metrics
            MaxSpeedMetersPerSecond = activity.MaxSpeedMps,
            AverageCadence = activity.AverageCadence,
            AverageTempCelsius = activity.AverageTempCelsius,
            SufferScore = ToNullableInt(activity.SufferScore),
            AchievementCount = activity.AchievementCount,
            PersonalRecordCount = activity.PrCount,
            KudosCount = activity.KudosCount,
            IsTrainer = activity.Trainer,
            IsCommute = activity.Commute,
            IsManual = activity.Manual,
            DeviceName = activity.DeviceName,
            GearName = ComposeGearLabel(gear),
            AverageWatts = activity.AverageWatts,
            MaxWatts = activity.MaxWatts,
            WeightedAverageWatts = activity.WeightedAverageWatts,
            Kilojoules = activity.Kilojoules,
            Splits = MapSplits(activity.SplitsMetric),
            Laps = MapLaps(activity.Laps),
            BestEfforts = MapBestEfforts(activity.BestEfforts)
        };
    }

    private static string? ComposeGearLabel(StravaGear? gear)
    {
        if (gear is null) return null;
        var nickname = gear.Nickname;
        var name = gear.Name;
        var primary = !string.IsNullOrWhiteSpace(nickname)
            ? nickname
            : !string.IsNullOrWhiteSpace(name)
                ? name
                : Combine(gear.BrandName, gear.ModelName);
        return string.IsNullOrWhiteSpace(primary) ? null : primary;
    }

    private static string? Combine(string? brand, string? model)
    {
        if (string.IsNullOrWhiteSpace(brand) && string.IsNullOrWhiteSpace(model)) return null;
        return string.Join(' ', new[] { brand, model }
            .Where(s => !string.IsNullOrWhiteSpace(s)));
    }

    private static IReadOnlyList<TrainingSplit> MapSplits(List<StravaSplit>? splits)
    {
        if (splits is null || splits.Count == 0) return Array.Empty<TrainingSplit>();
        return splits
            .Select(s =>
            {
                var duration = TimeSpan.FromSeconds(s.MovingTimeSeconds);
                return new TrainingSplit
                {
                    Index = s.SplitNumber,
                    DistanceMeters = s.DistanceMeters,
                    Duration = duration,
                    PaceSecondsPerKm = ComputePaceSecondsPerKm(s.DistanceMeters, duration),
                    AverageHeartRate = ToNullableInt(s.AverageHeartRate),
                    ElevationChangeMeters = s.ElevationDifferenceMeters,
                    PaceZone = s.PaceZone
                };
            })
            .ToList();
    }

    private static IReadOnlyList<TrainingLap> MapLaps(List<StravaLap>? laps)
    {
        if (laps is null || laps.Count == 0) return Array.Empty<TrainingLap>();
        return laps
            .Select(l => new TrainingLap
            {
                Index = l.LapIndex,
                Name = l.Name,
                DistanceMeters = l.DistanceMeters,
                Duration = TimeSpan.FromSeconds(l.MovingTimeSeconds),
                AverageHeartRate = ToNullableInt(l.AverageHeartRate),
                MaxHeartRate = ToNullableInt(l.MaxHeartRate),
                AverageCadence = l.AverageCadence,
                ElevationGainMeters = l.ElevationGainMeters
            })
            .ToList();
    }

    private static IReadOnlyList<TrainingBestEffort> MapBestEfforts(List<StravaBestEffort>? efforts)
    {
        if (efforts is null || efforts.Count == 0) return Array.Empty<TrainingBestEffort>();
        return efforts
            .Select(b => new TrainingBestEffort
            {
                Name = b.Name ?? string.Empty,
                DistanceMeters = b.DistanceMeters,
                Duration = TimeSpan.FromSeconds(b.MovingTimeSeconds),
                PersonalRecordRank = b.PrRank
            })
            .ToList();
    }

    private static string MapTypeToCategory(string sportType) => sportType.ToLowerInvariant() switch
    {
        "run" or "trailrun" or "virtualrun" => VideoCategories.Running,
        "hike" or "snowshoe" or "backcountryski" or "alpineski"
            => VideoCategories.Gory,
        "rockclimbing" or "rockclimb" or "climbing" => VideoCategories.WspinaczkaSkalowa,
        "bouldering" => VideoCategories.Bouldering,
        "indoorclimbing" or "gym" or "indoor" => VideoCategories.ProwadzieniHala,
        "weighttraining" or "workout" or "crossfit" or "calisthenics" or "yoga"
            => VideoCategories.Calisthenics,
        _ => VideoCategories.Running
    };

    private static double? ComputePaceSecondsPerKm(double distanceMeters, TimeSpan duration)
    {
        if (distanceMeters <= 0 || duration.TotalSeconds <= 0) return null;
        var kilometres = distanceMeters / 1000.0;
        return duration.TotalSeconds / kilometres;
    }

    private static int? ToNullableInt(double? value) =>
        value is null or 0 ? null : (int)Math.Round(value.Value);

    private static string ResolveDisplayType(StravaActivity activity) =>
        !string.IsNullOrWhiteSpace(activity.SportType)
            ? activity.SportType!
            : activity.Type;

    /// <summary>
    /// Extracts the start coordinates from a Strava activity. Strava returns
    /// the start position as a two-element <c>[lat, lng]</c> array, which is
    /// occasionally empty even for outdoor activities (auto-trim, paused
    /// start, indoor warm-up). When that happens the encoded summary polyline
    /// almost always still contains the full GPS track — TCX files Strava
    /// lets you download carry the same information — so the first decoded
    /// point becomes the fallback.
    /// </summary>
    /// <param name="activity">Activity returned by the Strava API.</param>
    /// <returns>A tuple of nullable doubles; both null when no GPS fix anywhere.</returns>
    public static (double? Latitude, double? Longitude) ExtractStartCoordinates(
        StravaActivity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);

        var coords = activity.StartLatLng;
        if (coords is { Length: >= 2 } && !(coords[0] == 0 && coords[1] == 0))
            return (coords[0], coords[1]);

        var fromPolyline = PolylineDecoder.FirstPoint(
            activity.Map?.SummaryPolyline ?? activity.Map?.Polyline);
        if (fromPolyline is null) return (null, null);
        if (fromPolyline.Value.Latitude == 0 && fromPolyline.Value.Longitude == 0)
            return (null, null);
        return (fromPolyline.Value.Latitude, fromPolyline.Value.Longitude);
    }

    /// <summary>
    /// Builds a human-readable location label from Strava's free-text fields,
    /// preferring the most specific tier available. Returns null when none of
    /// the fields are populated.
    /// </summary>
    /// <param name="activity">Activity returned by the Strava API.</param>
    public static string? ExtractLocationLabel(StravaActivity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        var parts = new List<string>(3);
        if (!string.IsNullOrWhiteSpace(activity.LocationCity))    parts.Add(activity.LocationCity!);
        if (!string.IsNullOrWhiteSpace(activity.LocationState))   parts.Add(activity.LocationState!);
        if (!string.IsNullOrWhiteSpace(activity.LocationCountry)) parts.Add(activity.LocationCountry!);
        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    private static readonly char[] VenueDelimiters = ['-', '–', '—', '|', '/', ':'];

    /// <summary>
    /// Attempts to pull a venue name out of the activity title using common
    /// delimiter conventions ("Avatar Kraków - Push Day", "Hala 100-lecia |
    /// climbing session"…). Returns the trimmed prefix when it looks like a
    /// proper name (starts with a capital, at least four characters) and is
    /// not a generic placeholder Strava auto-generates.
    /// </summary>
    /// <param name="activityName">Activity title as entered by the athlete.</param>
    public static string? ExtractVenueFromTitle(string? activityName)
    {
        if (string.IsNullOrWhiteSpace(activityName)) return null;
        var name = activityName.Trim();

        var idx = FindVenueDelimiterIndex(name);
        var candidate = idx > 0 ? name[..idx].Trim() : name;

        if (candidate.Length < 4) return null;
        if (!char.IsLetter(candidate[0]) || !char.IsUpper(candidate[0])) return null;
        if (IsGenericStravaTitle(candidate)) return null;

        return candidate;
    }

    /// <summary>
    /// Locates the first character that should split a venue from the rest
    /// of the title. Hyphens / en-dashes / em-dashes only count when they
    /// are flanked by spaces, otherwise hyphenated compound words like
    /// "Hala 100-lecia" are sliced in the middle.
    /// </summary>
    private static int FindVenueDelimiterIndex(string name)
    {
        for (var i = 0; i < name.Length; i++)
        {
            var ch = name[i];
            if (ch is '|' or '/' or ':')
                return i;

            if (ch is '-' or '–' or '—')
            {
                var hasSpaceBefore = i > 0 && char.IsWhiteSpace(name[i - 1]);
                var hasSpaceAfter = i + 1 < name.Length && char.IsWhiteSpace(name[i + 1]);
                if (hasSpaceBefore && hasSpaceAfter)
                    return i;
            }
        }
        return -1;
    }

    /// <summary>
    /// Cached set of generic Strava auto-titles built from the cross-product
    /// of {time-of-day} x {activity}. Lookup is O(1) and case-insensitive,
    /// and the data structure makes it trivial to add a new variant without
    /// touching control flow.
    /// </summary>
    private static readonly HashSet<string> GenericStravaTitles =
        BuildGenericStravaTitleSet();

    private static HashSet<string> BuildGenericStravaTitleSet()
    {
        var times = new[] { "morning", "afternoon", "evening", "lunch", "night" };
        var activities = new[]
        {
            "run", "ride", "walk", "hike", "swim",
            "workout", "weight training", "yoga"
        };
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in times)
            foreach (var a in activities)
                set.Add($"{t} {a}");
        return set;
    }

    private static bool IsGenericStravaTitle(string candidate) =>
        GenericStravaTitles.Contains(candidate);
}
