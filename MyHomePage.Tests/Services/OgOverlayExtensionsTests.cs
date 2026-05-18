namespace MyHomePage.Tests.Services;

/// <summary>
/// Tests for <see cref="OgOverlayExtensions.ToOgOverlay"/> — the helper
/// that turns a gallery <see cref="Video"/> into the
/// <see cref="OgOverlay"/> payload the OG renderer uses.
/// </summary>
[TestFixture]
public sealed class OgOverlayExtensionsTests
{
    [Test]
    public void ToOgOverlay_StravaActivityVideo_MapsEveryStatField()
    {
        var video = Video.Create(
            id: 1, title: "Morning Ride", description: "",
            fileName: "cover.jpg", location: "Kraków, Poland",
            category: VideoCategories.Bicycle, fileSizeBytes: 0);
        video.UploadedAt = new DateTime(2026, 5, 17, 0, 0, 0, DateTimeKind.Utc);
        video.Training = new TrainingData
        {
            Source = TrainingSource.Strava,
            ExternalId = "999",
            ActivityType = "Ride",
            StartTimeUtc = new DateTime(2026, 5, 17, 6, 30, 0, DateTimeKind.Utc),
            Duration = TimeSpan.FromMinutes(72),
            DistanceMeters = 42500,
            AveragePaceSecondsPerKm = 102, // 1:42/km
            Calories = 1450,
            ElevationGainMeters = 234
        };

        var overlay = video.ToOgOverlay();

        Assert.That(overlay, Is.Not.Null);
        Assert.That(overlay!.ActivityLabel, Is.EqualTo("Ride"));
        Assert.That(overlay.DistanceMeters, Is.EqualTo(42500));
        Assert.That(overlay.Duration, Is.EqualTo(TimeSpan.FromMinutes(72)));
        Assert.That(overlay.PaceSecondsPerKm, Is.EqualTo(102));
        Assert.That(overlay.Calories, Is.EqualTo(1450));
        Assert.That(overlay.ElevationGainMeters, Is.EqualTo(234));
        Assert.That(overlay.Location, Is.EqualTo("Kraków, Poland"));
        Assert.That(overlay.CapturedAt, Is.EqualTo(video.Training.StartTimeUtc));
    }

    [Test]
    public void ToOgOverlay_NoTraining_FallsBackToCategoryAndUploadedAt()
    {
        var video = Video.Create(
            id: 2, title: "Hike", description: "",
            fileName: "p.jpg", location: "Tatra Mountains",
            category: VideoCategories.Gory, fileSizeBytes: 0);
        video.UploadedAt = new DateTime(2026, 5, 10, 0, 0, 0, DateTimeKind.Utc);

        var overlay = video.ToOgOverlay();

        Assert.That(overlay, Is.Not.Null);
        Assert.That(overlay!.ActivityLabel, Is.EqualTo(VideoCategories.Gory));
        Assert.That(overlay.DistanceMeters, Is.Null);
        Assert.That(overlay.Duration, Is.Null);
        Assert.That(overlay.CapturedAt, Is.EqualTo(video.UploadedAt));
        Assert.That(overlay.Location, Is.EqualTo("Tatra Mountains"));
    }

    [Test]
    public void ToOgOverlay_TrainingWithZeroDuration_LeavesDurationNull()
    {
        var video = Video.Create(
            id: 3, title: "x", description: "", fileName: "p.jpg",
            location: null, category: VideoCategories.Running, fileSizeBytes: 0);
        video.Training = new TrainingData
        {
            Source = TrainingSource.Strava,
            ActivityType = "Run",
            Duration = TimeSpan.Zero,
            StartTimeUtc = new DateTime(2026, 5, 17, 0, 0, 0, DateTimeKind.Utc)
        };

        var overlay = video.ToOgOverlay();

        Assert.That(overlay, Is.Not.Null);
        Assert.That(overlay!.Duration, Is.Null);
    }

    [Test]
    public void ToOgOverlay_NoTrainingNoLocationNoCategory_ReturnsNull()
    {
        var video = Video.Create(
            id: 4, title: "Bare", description: "", fileName: "p.jpg",
            location: null, category: "", fileSizeBytes: 0);

        var overlay = video.ToOgOverlay();

        Assert.That(overlay, Is.Null);
    }

    [Test]
    public void ToOgOverlay_NullVideo_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => OgOverlayExtensions.ToOgOverlay(null!));
    }
}
