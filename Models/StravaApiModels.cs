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
    [JsonPropertyName("visibility")] public string? Visibility { get; set; }
    [JsonPropertyName("map")] public StravaActivityMap? Map { get; set; }
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
