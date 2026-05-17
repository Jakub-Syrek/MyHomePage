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

    [Test]
    public void ToTrainingData_WithSplitsMetric_MapsToOrderedTrainingSplits()
    {
        var activity = new StravaActivity
        {
            Id = 100,
            Type = "Run",
            SportType = "Run",
            StartDate = new DateTime(2026, 5, 15, 6, 0, 0, DateTimeKind.Utc),
            MovingTimeSeconds = 1500,
            DistanceMeters = 5000,
            SplitsMetric = new List<StravaSplit>
            {
                new()
                {
                    SplitNumber = 1, DistanceMeters = 1000,
                    MovingTimeSeconds = 290, ElapsedTimeSeconds = 290,
                    AverageHeartRate = 148, PaceZone = 2,
                    ElevationDifferenceMeters = 4
                },
                new()
                {
                    SplitNumber = 2, DistanceMeters = 1000,
                    MovingTimeSeconds = 300, ElapsedTimeSeconds = 300,
                    AverageHeartRate = 155, PaceZone = 3,
                    ElevationDifferenceMeters = -2
                }
            }
        };

        var training = StravaActivityMapper.ToTrainingData(activity);

        Assert.That(training.Splits, Has.Count.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(training.Splits[0].Index, Is.EqualTo(1));
            Assert.That(training.Splits[0].DistanceMeters, Is.EqualTo(1000));
            Assert.That(training.Splits[0].Duration, Is.EqualTo(TimeSpan.FromSeconds(290)));
            Assert.That(training.Splits[0].PaceSecondsPerKm, Is.EqualTo(290).Within(0.5));
            Assert.That(training.Splits[0].AverageHeartRate, Is.EqualTo(148));
            Assert.That(training.Splits[0].PaceZone, Is.EqualTo(2));
            Assert.That(training.Splits[0].ElevationChangeMeters, Is.EqualTo(4));
            Assert.That(training.Splits[1].PaceSecondsPerKm, Is.EqualTo(300).Within(0.5));
        });
    }

    [Test]
    public void ToTrainingData_WithLaps_MapsEveryLap()
    {
        var activity = new StravaActivity
        {
            Id = 101,
            Type = "Run",
            SportType = "Run",
            StartDate = new DateTime(2026, 5, 15, 6, 0, 0, DateTimeKind.Utc),
            MovingTimeSeconds = 2400,
            DistanceMeters = 8000,
            Laps = new List<StravaLap>
            {
                new()
                {
                    Id = 1, Name = "Warm up", LapIndex = 1,
                    DistanceMeters = 1500, MovingTimeSeconds = 540,
                    ElapsedTimeSeconds = 540, AverageHeartRate = 132,
                    MaxHeartRate = 145, AverageCadence = 84.5,
                    ElevationGainMeters = 12
                },
                new()
                {
                    Id = 2, Name = "Tempo", LapIndex = 2,
                    DistanceMeters = 5000, MovingTimeSeconds = 1500,
                    ElapsedTimeSeconds = 1500, AverageHeartRate = 168,
                    MaxHeartRate = 178, AverageCadence = 88.0,
                    ElevationGainMeters = 8
                }
            }
        };

        var training = StravaActivityMapper.ToTrainingData(activity);

        Assert.That(training.Laps, Has.Count.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(training.Laps[0].Index, Is.EqualTo(1));
            Assert.That(training.Laps[0].Name, Is.EqualTo("Warm up"));
            Assert.That(training.Laps[0].Duration, Is.EqualTo(TimeSpan.FromSeconds(540)));
            Assert.That(training.Laps[0].AverageHeartRate, Is.EqualTo(132));
            Assert.That(training.Laps[0].MaxHeartRate, Is.EqualTo(145));
            Assert.That(training.Laps[1].Name, Is.EqualTo("Tempo"));
            Assert.That(training.Laps[1].AverageCadence, Is.EqualTo(88.0));
        });
    }

    [Test]
    public void ToTrainingData_WithBestEfforts_RetainsPrRank()
    {
        var activity = new StravaActivity
        {
            Id = 102,
            Type = "Run",
            SportType = "Run",
            StartDate = new DateTime(2026, 5, 15, 6, 0, 0, DateTimeKind.Utc),
            MovingTimeSeconds = 1800,
            DistanceMeters = 5000,
            BestEfforts = new List<StravaBestEffort>
            {
                new()
                {
                    Id = 1, Name = "1k",
                    DistanceMeters = 1000, MovingTimeSeconds = 240,
                    ElapsedTimeSeconds = 240, PrRank = 1
                },
                new()
                {
                    Id = 2, Name = "5k",
                    DistanceMeters = 5000, MovingTimeSeconds = 1450,
                    ElapsedTimeSeconds = 1450, PrRank = null
                }
            }
        };

        var training = StravaActivityMapper.ToTrainingData(activity);

        Assert.That(training.BestEfforts, Has.Count.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(training.BestEfforts[0].Name, Is.EqualTo("1k"));
            Assert.That(training.BestEfforts[0].PersonalRecordRank, Is.EqualTo(1));
            Assert.That(training.BestEfforts[1].PersonalRecordRank, Is.Null);
        });
    }

    [Test]
    public void ToTrainingData_WithExtendedMetrics_PopulatesEveryField()
    {
        var activity = new StravaActivity
        {
            Id = 103,
            Type = "Ride",
            SportType = "Ride",
            StartDate = new DateTime(2026, 5, 15, 6, 0, 0, DateTimeKind.Utc),
            MovingTimeSeconds = 3600,
            DistanceMeters = 30000,
            MaxSpeedMps = 14.5,
            AverageCadence = 82.4,
            AverageTempCelsius = 18.5,
            SufferScore = 165,
            AchievementCount = 2,
            PrCount = 1,
            KudosCount = 7,
            Trainer = false,
            Commute = true,
            Manual = false,
            DeviceName = "Garmin Edge 540",
            AverageWatts = 178,
            MaxWatts = 412,
            WeightedAverageWatts = 195,
            Kilojoules = 650
        };

        var training = StravaActivityMapper.ToTrainingData(activity);

        Assert.Multiple(() =>
        {
            Assert.That(training.MaxSpeedMetersPerSecond, Is.EqualTo(14.5));
            Assert.That(training.AverageCadence, Is.EqualTo(82.4));
            Assert.That(training.AverageTempCelsius, Is.EqualTo(18.5));
            Assert.That(training.SufferScore, Is.EqualTo(165));
            Assert.That(training.AchievementCount, Is.EqualTo(2));
            Assert.That(training.PersonalRecordCount, Is.EqualTo(1));
            Assert.That(training.KudosCount, Is.EqualTo(7));
            Assert.That(training.IsTrainer, Is.False);
            Assert.That(training.IsCommute, Is.True);
            Assert.That(training.IsManual, Is.False);
            Assert.That(training.DeviceName, Is.EqualTo("Garmin Edge 540"));
            Assert.That(training.AverageWatts, Is.EqualTo(178));
            Assert.That(training.MaxWatts, Is.EqualTo(412));
            Assert.That(training.WeightedAverageWatts, Is.EqualTo(195));
            Assert.That(training.Kilojoules, Is.EqualTo(650));
        });
    }

    [Test]
    public void ToTrainingData_GearWithNickname_PrefersNicknameOverName()
    {
        var activity = new StravaActivity
        {
            Id = 104, Type = "Run", SportType = "Run",
            StartDate = new DateTime(2026, 5, 15, 6, 0, 0, DateTimeKind.Utc),
            MovingTimeSeconds = 1800, DistanceMeters = 5000
        };
        var gear = new StravaGear
        {
            Id = "g1",
            Nickname = "Hokas",
            Name = "Hoka Mach 6",
            BrandName = "Hoka",
            ModelName = "Mach 6"
        };

        var training = StravaActivityMapper.ToTrainingData(activity, gear);

        Assert.That(training.GearName, Is.EqualTo("Hokas"));
    }

    [Test]
    public void ToTrainingData_GearWithoutNickname_FallsBackToFullName()
    {
        var activity = new StravaActivity
        {
            Id = 105, Type = "Ride", SportType = "Ride",
            StartDate = new DateTime(2026, 5, 15, 6, 0, 0, DateTimeKind.Utc),
            MovingTimeSeconds = 3600, DistanceMeters = 30000
        };
        var gear = new StravaGear
        {
            Id = "b1",
            Nickname = null,
            Name = "Bike",
            BrandName = "Specialized",
            ModelName = "Allez Sprint"
        };

        var training = StravaActivityMapper.ToTrainingData(activity, gear);

        Assert.That(training.GearName, Is.EqualTo("Bike"));
    }

    [Test]
    public void ToTrainingData_GearWithoutNicknameOrName_BuildsBrandModel()
    {
        var activity = new StravaActivity
        {
            Id = 106, Type = "Ride", SportType = "Ride",
            StartDate = new DateTime(2026, 5, 15, 6, 0, 0, DateTimeKind.Utc),
            MovingTimeSeconds = 3600, DistanceMeters = 30000
        };
        var gear = new StravaGear
        {
            Id = "b2",
            BrandName = "Trek",
            ModelName = "Domane SL"
        };

        var training = StravaActivityMapper.ToTrainingData(activity, gear);

        Assert.That(training.GearName, Is.EqualTo("Trek Domane SL"));
    }

    [Test]
    public void ToTrainingData_WithoutGear_LeavesGearNameNull()
    {
        var activity = new StravaActivity
        {
            Id = 107, Type = "Run", SportType = "Run",
            StartDate = new DateTime(2026, 5, 15, 6, 0, 0, DateTimeKind.Utc),
            MovingTimeSeconds = 1800, DistanceMeters = 5000
        };

        var training = StravaActivityMapper.ToTrainingData(activity, gear: null);

        Assert.That(training.GearName, Is.Null);
    }
}
