using System.Text.Json;
using Microsoft.AspNetCore.Components.Forms;

namespace MyHomePage.Tests.Models;

/// <summary>
/// Sanity tests for the small data carriers — make sure the JSON
/// attribute mapping, the default values and the convenience constructors
/// stay correct as the schemas evolve.
/// </summary>
[TestFixture]
public sealed class DtoSanityTests
{
    // ── UploadSuggestion ──────────────────────────────────────────────────

    [Test]
    public void UploadSuggestion_Defaults_AreEmpty()
    {
        var s = new UploadSuggestion();

        Assert.That(s.Title, Is.EqualTo(""));
        Assert.That(s.Description, Is.EqualTo(""));
        Assert.That(s.Location, Is.Null);
        Assert.That(s.Latitude, Is.Null);
        Assert.That(s.Longitude, Is.Null);
    }

    [Test]
    public void UploadSuggestion_AllowsLocationAndCoordinates()
    {
        var s = new UploadSuggestion
        {
            Title = "T", Description = "D",
            Location = "Kraków", Latitude = 50.06, Longitude = 19.94
        };

        Assert.That(s.Title, Is.EqualTo("T"));
        Assert.That(s.Latitude, Is.EqualTo(50.06));
        Assert.That(s.Longitude, Is.EqualTo(19.94));
        Assert.That(s.Location, Is.EqualTo("Kraków"));
    }

    // ── VideoUploadRequest ────────────────────────────────────────────────

    [Test]
    public void VideoUploadRequest_SingleFileCtor_WrapsFileInList()
    {
        var file = Substitute.For<IBrowserFile>();
        file.Name.Returns("clip.mp4");

        var request = new VideoUploadRequest(
            file, "Title", "Desc", "Krakow", VideoCategories.Running);

        Assert.That(request.Files, Has.Count.EqualTo(1));
        Assert.That(request.Files[0], Is.SameAs(file));
        Assert.That(request.Title, Is.EqualTo("Title"));
        Assert.That(request.Category, Is.EqualTo(VideoCategories.Running));
    }

    [Test]
    public void VideoUploadRequest_MultiFileCtor_PreservesOrder()
    {
        var f1 = Substitute.For<IBrowserFile>();
        var f2 = Substitute.For<IBrowserFile>();

        var request = new VideoUploadRequest(
            new[] { f1, f2 }, "T", "D", null,
            VideoCategories.Bouldering, 50.0, 19.0);

        Assert.That(request.Files, Has.Count.EqualTo(2));
        Assert.That(request.Files[0], Is.SameAs(f1));
        Assert.That(request.Files[1], Is.SameAs(f2));
        Assert.That(request.Latitude, Is.EqualTo(50.0));
        Assert.That(request.Longitude, Is.EqualTo(19.0));
    }

    // ── StravaTokenResponse ───────────────────────────────────────────────

    [Test]
    public void StravaTokenResponse_DeserialisesSnakeCaseFromStrava()
    {
        const string json = """
        {
            "token_type": "Bearer",
            "access_token": "AT",
            "refresh_token": "RT",
            "expires_at": 1900000000,
            "scope": "read,activity:read_all",
            "athlete": { "id": 99 }
        }
        """;

        var tr = JsonSerializer.Deserialize<StravaTokenResponse>(json)!;

        Assert.That(tr.TokenType, Is.EqualTo("Bearer"));
        Assert.That(tr.AccessToken, Is.EqualTo("AT"));
        Assert.That(tr.RefreshToken, Is.EqualTo("RT"));
        Assert.That(tr.ExpiresAt, Is.EqualTo(1900000000));
        Assert.That(tr.Athlete!.Id, Is.EqualTo(99));
    }

    // ── StravaActivity ────────────────────────────────────────────────────

    [Test]
    public void StravaActivity_DeserialisesAllExtendedFields()
    {
        const string json = """
        {
            "id": 7, "name": "Run",
            "type": "Run", "sport_type": "Run",
            "start_date": "2026-05-15T06:00:00Z",
            "moving_time": 1800, "elapsed_time": 1900,
            "distance": 5000,
            "total_elevation_gain": 25,
            "average_heartrate": 150, "max_heartrate": 175,
            "calories": 320,
            "average_cadence": 85,
            "average_temp": 14,
            "suffer_score": 80,
            "achievement_count": 2, "pr_count": 1, "kudos_count": 4,
            "trainer": false, "commute": true, "manual": false,
            "device_name": "Garmin",
            "gear_id": "g42",
            "average_watts": 220, "weighted_average_watts": 230,
            "kilojoules": 800,
            "visibility": "everyone",
            "start_latlng": [50.06, 19.94],
            "location_city": "Kraków",
            "location_country": "Poland"
        }
        """;

        var a = JsonSerializer.Deserialize<StravaActivity>(json)!;

        Assert.That(a.Id, Is.EqualTo(7));
        Assert.That(a.AverageCadence, Is.EqualTo(85));
        Assert.That(a.SufferScore, Is.EqualTo(80));
        Assert.That(a.AchievementCount, Is.EqualTo(2));
        Assert.That(a.KudosCount, Is.EqualTo(4));
        Assert.That(a.Commute, Is.True);
        Assert.That(a.GearId, Is.EqualTo("g42"));
        Assert.That(a.Kilojoules, Is.EqualTo(800));
        Assert.That(a.LocationCity, Is.EqualTo("Kraków"));
        Assert.That(a.StartLatLng, Is.EqualTo(new[] { 50.06, 19.94 }));
    }

    // ── StravaGear ────────────────────────────────────────────────────────

    [Test]
    public void StravaGear_DeserialisesNicknameAndBrand()
    {
        const string json = """
        { "id": "g1", "nickname": "Trail shoes",
          "brand_name": "Salomon", "model_name": "Speedcross",
          "distance": 1234.5, "primary": true, "resource_state": 3 }
        """;

        var g = JsonSerializer.Deserialize<StravaGear>(json)!;

        Assert.That(g.Id, Is.EqualTo("g1"));
        Assert.That(g.Nickname, Is.EqualTo("Trail shoes"));
        Assert.That(g.BrandName, Is.EqualTo("Salomon"));
        Assert.That(g.DistanceMeters, Is.EqualTo(1234.5));
        Assert.That(g.Primary, Is.True);
    }

    // ── StravaActivityMap / Webhook ───────────────────────────────────────

    [Test]
    public void StravaActivityMap_DeserialisesPolylines()
    {
        const string json = """
        { "id": "m1", "polyline": "_p~iF~ps|U", "summary_polyline": "abc" }
        """;

        var m = JsonSerializer.Deserialize<StravaActivityMap>(json)!;

        Assert.That(m.Id, Is.EqualTo("m1"));
        Assert.That(m.Polyline, Is.EqualTo("_p~iF~ps|U"));
        Assert.That(m.SummaryPolyline, Is.EqualTo("abc"));
    }

    [Test]
    public void StravaWebhookEvent_DeserialisesPushEvent()
    {
        const string json = """
        { "object_type": "activity", "object_id": 12345,
          "aspect_type": "create", "owner_id": 9,
          "subscription_id": 100, "event_time": 1700000000 }
        """;

        var e = JsonSerializer.Deserialize<StravaWebhookEvent>(json)!;

        Assert.That(e.ObjectType, Is.EqualTo("activity"));
        Assert.That(e.ObjectId, Is.EqualTo(12345));
        Assert.That(e.AspectType, Is.EqualTo("create"));
        Assert.That(e.EventTimeSeconds, Is.EqualTo(1700000000));
    }

    // ── StravaSplit / StravaLap / StravaBestEffort ───────────────────────

    [Test]
    public void StravaSplit_DeserialisesPaceZoneAndElevation()
    {
        const string json = """
        { "split": 1, "distance": 1000, "moving_time": 360,
          "elapsed_time": 360, "elevation_difference": 5,
          "average_speed": 2.78, "average_heartrate": 152, "pace_zone": 2 }
        """;

        var s = JsonSerializer.Deserialize<StravaSplit>(json)!;

        Assert.That(s.SplitNumber, Is.EqualTo(1));
        Assert.That(s.ElevationDifferenceMeters, Is.EqualTo(5));
        Assert.That(s.PaceZone, Is.EqualTo(2));
    }

    [Test]
    public void StravaLap_DeserialisesCadenceAndWatts()
    {
        const string json = """
        { "id": 99, "name": "Lap 1", "lap_index": 0,
          "distance": 400, "moving_time": 90, "elapsed_time": 90,
          "total_elevation_gain": 1, "average_speed": 4.44, "max_speed": 5,
          "average_heartrate": 165, "max_heartrate": 180,
          "average_cadence": 90, "average_watts": 250 }
        """;

        var l = JsonSerializer.Deserialize<StravaLap>(json)!;

        Assert.That(l.Id, Is.EqualTo(99));
        Assert.That(l.AverageCadence, Is.EqualTo(90));
        Assert.That(l.AverageWatts, Is.EqualTo(250));
    }

    [Test]
    public void StravaBestEffort_DeserialisesPrRank()
    {
        const string json = """
        { "id": 7, "name": "1k", "distance": 1000,
          "moving_time": 240, "elapsed_time": 240, "pr_rank": 1 }
        """;

        var b = JsonSerializer.Deserialize<StravaBestEffort>(json)!;

        Assert.That(b.Name, Is.EqualTo("1k"));
        Assert.That(b.PrRank, Is.EqualTo(1));
    }

    // ── StravaTokenSet computed ───────────────────────────────────────────

    [Test]
    public void StravaTokenSet_IsExpired_FuturePlusMargin_IsFalse()
    {
        var token = new StravaTokenSet
        {
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(10)
        };

        Assert.That(token.IsExpired, Is.False);
    }

    [Test]
    public void StravaTokenSet_IsExpired_AlreadyPast_IsTrue()
    {
        var token = new StravaTokenSet
        {
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1)
        };

        Assert.That(token.IsExpired, Is.True);
    }

    [Test]
    public void StravaTokenSet_IsExpired_WithinSafetyMargin_IsTrue()
    {
        // The model treats anything within 60s of expiry as expired so
        // refresh fires preemptively.
        var token = new StravaTokenSet
        {
            ExpiresAtUtc = DateTime.UtcNow.AddSeconds(30)
        };

        Assert.That(token.IsExpired, Is.True);
    }
}
