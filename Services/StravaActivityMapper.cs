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
    /// stored alongside a gallery item.
    /// </summary>
    /// <param name="activity">Activity returned by the Strava API.</param>
    public static TrainingData ToTrainingData(StravaActivity activity)
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
            ExternalUrl = $"https://www.strava.com/activities/{activity.Id}"
        };
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
    /// the start position as a two-element <c>[lat, lng]</c> array which is
    /// empty for activities recorded without GPS (e.g. indoor weight training).
    /// </summary>
    /// <param name="activity">Activity returned by the Strava API.</param>
    /// <returns>A tuple of nullable doubles; both null when no GPS fix.</returns>
    public static (double? Latitude, double? Longitude) ExtractStartCoordinates(
        StravaActivity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        var coords = activity.StartLatLng;
        if (coords is null || coords.Length < 2) return (null, null);
        if (coords[0] == 0 && coords[1] == 0) return (null, null);
        return (coords[0], coords[1]);
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
}
