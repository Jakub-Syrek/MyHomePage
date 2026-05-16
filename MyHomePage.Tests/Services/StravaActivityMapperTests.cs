namespace MyHomePage.Tests.Services;

/// <summary>
/// Unit tests for <see cref="StravaActivityMapper"/>. The mapper is stateless
/// so the tests only need plain data fixtures — no DI container, no mocks.
/// </summary>
[TestFixture]
public sealed class StravaActivityMapperTests
{
    [TestCase("Run", VideoCategories.Running)]
    [TestCase("TrailRun", VideoCategories.Running)]
    [TestCase("VirtualRun", VideoCategories.Running)]
    [TestCase("Hike", VideoCategories.Gory)]
    [TestCase("Snowshoe", VideoCategories.Gory)]
    [TestCase("RockClimbing", VideoCategories.WspinaczkaSkalowa)]
    [TestCase("Bouldering", VideoCategories.Bouldering)]
    [TestCase("IndoorClimbing", VideoCategories.ProwadzieniHala)]
    [TestCase("WeightTraining", VideoCategories.Calisthenics)]
    [TestCase("Workout", VideoCategories.Calisthenics)]
    [TestCase("Crossfit", VideoCategories.Calisthenics)]
    [TestCase("Yoga", VideoCategories.Calisthenics)]
    public void ResolveCategory_KnownActivityType_MapsToExpectedCategory(
        string sportType, string expected)
    {
        var activity = new StravaActivity { SportType = sportType, Type = "Fallback" };

        var category = StravaActivityMapper.ResolveCategory(activity);

        Assert.That(category, Is.EqualTo(expected));
    }

    [Test]
    public void ResolveCategory_UnknownType_FallsBackToRunning()
    {
        var activity = new StravaActivity { SportType = "EBikeRide" };

        var category = StravaActivityMapper.ResolveCategory(activity);

        Assert.That(category, Is.EqualTo(VideoCategories.Running));
    }

    [Test]
    public void ToTrainingData_TypicalRun_PopulatesAllNumericMetrics()
    {
        var start = new DateTime(2026, 5, 15, 6, 0, 0, DateTimeKind.Utc);
        var activity = new StravaActivity
        {
            Id = 4242,
            Name = "Morning Run",
            Type = "Run",
            SportType = "Run",
            StartDate = start,
            MovingTimeSeconds = 1800,
            DistanceMeters = 5000,
            ElevationGainMeters = 42,
            AverageHeartRate = 155,
            MaxHeartRate = 178,
            Calories = 420,
            Map = new StravaActivityMap { SummaryPolyline = "abc123" }
        };

        var training = StravaActivityMapper.ToTrainingData(activity);

        Assert.Multiple(() =>
        {
            Assert.That(training.Source, Is.EqualTo(TrainingSource.Strava));
            Assert.That(training.ExternalId, Is.EqualTo("4242"));
            Assert.That(training.ActivityType, Is.EqualTo("Run"));
            Assert.That(training.StartTimeUtc.Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(training.Duration, Is.EqualTo(TimeSpan.FromMinutes(30)));
            Assert.That(training.DistanceMeters, Is.EqualTo(5000));
            Assert.That(training.AveragePaceSecondsPerKm, Is.EqualTo(360).Within(0.5));
            Assert.That(training.ElevationGainMeters, Is.EqualTo(42));
            Assert.That(training.AverageHeartRate, Is.EqualTo(155));
            Assert.That(training.MaxHeartRate, Is.EqualTo(178));
            Assert.That(training.Calories, Is.EqualTo(420));
            Assert.That(training.RoutePolyline, Is.EqualTo("abc123"));
            Assert.That(training.ExternalUrl, Is.EqualTo("https://www.strava.com/activities/4242"));
        });
    }

    [Test]
    public void ToTrainingData_NoDistance_LeavesPaceNull()
    {
        var activity = new StravaActivity
        {
            Id = 1,
            Type = "WeightTraining",
            MovingTimeSeconds = 1800,
            DistanceMeters = 0
        };

        var training = StravaActivityMapper.ToTrainingData(activity);

        Assert.That(training.AveragePaceSecondsPerKm, Is.Null);
        Assert.That(training.DistanceMeters, Is.Null);
    }

    [Test]
    public void ExtractStartCoordinates_ValidLatLng_ReturnsTuple()
    {
        var activity = new StravaActivity
        {
            StartLatLng = new[] { 50.0614, 19.9366 }
        };

        var (lat, lng) = StravaActivityMapper.ExtractStartCoordinates(activity);

        Assert.That(lat, Is.EqualTo(50.0614));
        Assert.That(lng, Is.EqualTo(19.9366));
    }

    [TestCase(null)]
    [TestCase(new double[] { })]
    [TestCase(new[] { 0.0, 0.0 })]
    public void ExtractStartCoordinates_MissingOrZero_ReturnsNulls(double[]? coords)
    {
        var activity = new StravaActivity { StartLatLng = coords };

        var (lat, lng) = StravaActivityMapper.ExtractStartCoordinates(activity);

        Assert.That(lat, Is.Null);
        Assert.That(lng, Is.Null);
    }

    [Test]
    public void ExtractStartCoordinates_StartLatLngEmptyButPolylinePresent_FallsBackToFirstPoint()
    {
        var activity = new StravaActivity
        {
            StartLatLng = new[] { 0.0, 0.0 },
            Map = new StravaActivityMap
            {
                // Canonical Google polyline sample: 38.5, -120.2 / 40.7, -120.95 / 43.252, -126.453
                SummaryPolyline = "_p~iF~ps|U_ulLnnqC_mqNvxq`@"
            }
        };

        var (lat, lng) = StravaActivityMapper.ExtractStartCoordinates(activity);

        Assert.That(lat, Is.EqualTo(38.5).Within(0.001));
        Assert.That(lng, Is.EqualTo(-120.2).Within(0.001));
    }

    [Test]
    public void ExtractLocationLabel_AllFieldsPresent_JoinsWithComma()
    {
        var activity = new StravaActivity
        {
            LocationCity = "Krakow",
            LocationState = "Lesser Poland",
            LocationCountry = "Poland"
        };

        var label = StravaActivityMapper.ExtractLocationLabel(activity);

        Assert.That(label, Is.EqualTo("Krakow, Lesser Poland, Poland"));
    }

    [Test]
    public void ExtractLocationLabel_AllFieldsEmpty_ReturnsNull()
    {
        var activity = new StravaActivity();

        var label = StravaActivityMapper.ExtractLocationLabel(activity);

        Assert.That(label, Is.Null);
    }

    [TestCase("Avatar Kraków - Push Day", "Avatar Kraków")]
    [TestCase("Hala 100-lecia | climbing session", "Hala 100-lecia")]
    [TestCase("Forum Climbing Gym Berlin / lower body", "Forum Climbing Gym Berlin")]
    [TestCase("Avatar Kraków: legs", "Avatar Kraków")]
    public void ExtractVenueFromTitle_DelimiterPrefix_ReturnsTrimmedVenue(
        string title, string expected)
    {
        var venue = StravaActivityMapper.ExtractVenueFromTitle(title);

        Assert.That(venue, Is.EqualTo(expected));
    }

    [TestCase("Morning Run")]
    [TestCase("Afternoon Ride")]
    [TestCase("Evening Weight Training")]
    [TestCase("Lunch Workout")]
    [TestCase("Night Yoga")]
    public void ExtractVenueFromTitle_GenericStravaTitle_ReturnsNull(string title)
    {
        var venue = StravaActivityMapper.ExtractVenueFromTitle(title);

        Assert.That(venue, Is.Null);
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("ab")]
    [TestCase("lowercase venue - body")]
    public void ExtractVenueFromTitle_TooShortOrNotProperName_ReturnsNull(string? title)
    {
        var venue = StravaActivityMapper.ExtractVenueFromTitle(title);

        Assert.That(venue, Is.Null);
    }
}
