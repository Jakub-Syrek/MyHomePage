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
}
