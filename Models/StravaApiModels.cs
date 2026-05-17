using System.Text.Json.Serialization;

namespace MyHomePage.Models;

/// <summary>
/// Slim DTOs covering only the Strava REST fields the integration actually
/// consumes. Defined here (rather than inside the service) so they can be
/// re-used by tests without taking a dependency on Strava's full SDK.
/// </summary>
internal static class StravaApiModels { }

/// <summary>Raw token-exchange / refresh response from <c>oauth/token</c>.</summary>
public sealed class StravaTokenResponse
{
    [JsonPropertyName("token_type")] public string TokenType { get; set; } = string.Empty;
    [JsonPropertyName("access_token")] public string AccessToken { get; set; } = string.Empty;
    [JsonPropertyName("refresh_token")] public string RefreshToken { get; set; } = string.Empty;
    [JsonPropertyName("expires_at")] public long ExpiresAt { get; set; }
    [JsonPropertyName("scope")] public string Scope { get; set; } = string.Empty;
    [JsonPropertyName("athlete")] public StravaAthlete? Athlete { get; set; }
}

/// <summary>Athlete payload nested inside the initial token exchange response.</summary>
public sealed class StravaAthlete
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("username")] public string? Username { get; set; }
    [JsonPropertyName("firstname")] public string? FirstName { get; set; }
    [JsonPropertyName("lastname")] public string? LastName { get; set; }
}

/// <summary>Activity payload returned by <c>GET /api/v3/activities/{id}</c>.</summary>
public sealed class StravaActivity
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("type")] public string Type { get; set; } = string.Empty;
    [JsonPropertyName("sport_type")] public string? SportType { get; set; }
    [JsonPropertyName("start_date")] public DateTime StartDate { get; set; }
    [JsonPropertyName("moving_time")] public int MovingTimeSeconds { get; set; }
    [JsonPropertyName("elapsed_time")] public int ElapsedTimeSeconds { get; set; }
    [JsonPropertyName("distance")] public double DistanceMeters { get; set; }
    [JsonPropertyName("total_elevation_gain")] public double? ElevationGainMeters { get; set; }
    [JsonPropertyName("average_heartrate")] public double? AverageHeartRate { get; set; }
    [JsonPropertyName("max_heartrate")] public double? MaxHeartRate { get; set; }
    [JsonPropertyName("calories")] public double? Calories { get; set; }
    [JsonPropertyName("average_speed")] public double? AverageSpeedMps { get; set; }
    [JsonPropertyName("max_speed")] public double? MaxSpeedMps { get; set; }
    [JsonPropertyName("average_cadence")] public double? AverageCadence { get; set; }
    [JsonPropertyName("average_temp")] public double? AverageTempCelsius { get; set; }
    [JsonPropertyName("suffer_score")] public double? SufferScore { get; set; }
    [JsonPropertyName("achievement_count")] public int? AchievementCount { get; set; }
    [JsonPropertyName("pr_count")] public int? PrCount { get; set; }
    [JsonPropertyName("kudos_count")] public int? KudosCount { get; set; }
    [JsonPropertyName("athlete_count")] public int? AthleteCount { get; set; }
    [JsonPropertyName("trainer")] public bool? Trainer { get; set; }
    [JsonPropertyName("commute")] public bool? Commute { get; set; }
    [JsonPropertyName("manual")] public bool? Manual { get; set; }
    [JsonPropertyName("device_name")] public string? DeviceName { get; set; }
    [JsonPropertyName("gear_id")] public string? GearId { get; set; }
    [JsonPropertyName("average_watts")] public double? AverageWatts { get; set; }
    [JsonPropertyName("max_watts")] public double? MaxWatts { get; set; }
    [JsonPropertyName("weighted_average_watts")] public double? WeightedAverageWatts { get; set; }
    [JsonPropertyName("kilojoules")] public double? Kilojoules { get; set; }
    [JsonPropertyName("device_watts")] public bool? DeviceWatts { get; set; }
    [JsonPropertyName("visibility")] public string? Visibility { get; set; }
    [JsonPropertyName("map")] public StravaActivityMap? Map { get; set; }
    [JsonPropertyName("start_latlng")] public double[]? StartLatLng { get; set; }
    [JsonPropertyName("end_latlng")] public double[]? EndLatLng { get; set; }
    [JsonPropertyName("location_city")] public string? LocationCity { get; set; }
    [JsonPropertyName("location_state")] public string? LocationState { get; set; }
    [JsonPropertyName("location_country")] public string? LocationCountry { get; set; }
    [JsonPropertyName("splits_metric")] public List<StravaSplit>? SplitsMetric { get; set; }
    [JsonPropertyName("laps")] public List<StravaLap>? Laps { get; set; }
    [JsonPropertyName("best_efforts")] public List<StravaBestEffort>? BestEfforts { get; set; }
}

/// <summary>Per-kilometer split exposed by Strava's <c>splits_metric</c> array.</summary>
public sealed class StravaSplit
{
    [JsonPropertyName("split")] public int SplitNumber { get; set; }
    [JsonPropertyName("distance")] public double DistanceMeters { get; set; }
    [JsonPropertyName("moving_time")] public int MovingTimeSeconds { get; set; }
    [JsonPropertyName("elapsed_time")] public int ElapsedTimeSeconds { get; set; }
    [JsonPropertyName("elevation_difference")] public double? ElevationDifferenceMeters { get; set; }
    [JsonPropertyName("average_speed")] public double? AverageSpeedMps { get; set; }
    [JsonPropertyName("average_heartrate")] public double? AverageHeartRate { get; set; }
    [JsonPropertyName("pace_zone")] public int? PaceZone { get; set; }
}

/// <summary>Manually-marked lap exposed by Strava's <c>laps</c> array.</summary>
public sealed class StravaLap
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("lap_index")] public int LapIndex { get; set; }
    [JsonPropertyName("distance")] public double DistanceMeters { get; set; }
    [JsonPropertyName("moving_time")] public int MovingTimeSeconds { get; set; }
    [JsonPropertyName("elapsed_time")] public int ElapsedTimeSeconds { get; set; }
    [JsonPropertyName("total_elevation_gain")] public double? ElevationGainMeters { get; set; }
    [JsonPropertyName("average_speed")] public double? AverageSpeedMps { get; set; }
    [JsonPropertyName("max_speed")] public double? MaxSpeedMps { get; set; }
    [JsonPropertyName("average_heartrate")] public double? AverageHeartRate { get; set; }
    [JsonPropertyName("max_heartrate")] public double? MaxHeartRate { get; set; }
    [JsonPropertyName("average_cadence")] public double? AverageCadence { get; set; }
    [JsonPropertyName("average_watts")] public double? AverageWatts { get; set; }
}

/// <summary>Best-effort segment recorded inside an activity (e.g. 1k / 5k / 10k PRs).</summary>
public sealed class StravaBestEffort
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("distance")] public double DistanceMeters { get; set; }
    [JsonPropertyName("moving_time")] public int MovingTimeSeconds { get; set; }
    [JsonPropertyName("elapsed_time")] public int ElapsedTimeSeconds { get; set; }
    [JsonPropertyName("pr_rank")] public int? PrRank { get; set; }
}

/// <summary>Athlete gear (shoes / bike) returned by <c>GET /api/v3/gear/{id}</c>.</summary>
public sealed class StravaGear
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("brand_name")] public string? BrandName { get; set; }
    [JsonPropertyName("model_name")] public string? ModelName { get; set; }
    [JsonPropertyName("distance")] public double? DistanceMeters { get; set; }
    [JsonPropertyName("primary")] public bool? Primary { get; set; }
    [JsonPropertyName("nickname")] public string? Nickname { get; set; }
    [JsonPropertyName("resource_state")] public int? ResourceState { get; set; }
}

/// <summary>Encoded polyline payload nested inside an activity response.</summary>
public sealed class StravaActivityMap
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("polyline")] public string? Polyline { get; set; }
    [JsonPropertyName("summary_polyline")] public string? SummaryPolyline { get; set; }
}

/// <summary>
/// Push event sent by Strava when an activity is created, updated or deleted.
/// See https://developers.strava.com/docs/webhooks/ for the schema.
/// </summary>
public sealed class StravaWebhookEvent
{
    [JsonPropertyName("object_type")] public string ObjectType { get; set; } = string.Empty;
    [JsonPropertyName("object_id")] public long ObjectId { get; set; }
    [JsonPropertyName("aspect_type")] public string AspectType { get; set; } = string.Empty;
    [JsonPropertyName("owner_id")] public long OwnerId { get; set; }
    [JsonPropertyName("subscription_id")] public long SubscriptionId { get; set; }
    [JsonPropertyName("event_time")] public long EventTimeSeconds { get; set; }
}
