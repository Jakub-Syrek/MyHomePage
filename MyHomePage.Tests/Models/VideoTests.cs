namespace MyHomePage.Tests.Models;

/// <summary>
/// Tests for the <see cref="Video"/> model: factory defaults, computed
/// flags (<c>HasCoordinates</c>, <c>HasTraining</c>) and the
/// <c>GetAllMedia</c> normalisation that bridges legacy single-file items
/// to the new multi-media schema.
/// </summary>
[TestFixture]
public sealed class VideoTests
{
    [Test]
    public void Create_PopulatesRequiredFieldsAndDefaults()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);

        var video = Video.Create(
            id: 7, title: "Solo run", description: "wind in face",
            fileName: "run.mp4", location: "Kraków",
            category: VideoCategories.Running, fileSizeBytes: 12_345);

        Assert.That(video.Id, Is.EqualTo(7));
        Assert.That(video.Title, Is.EqualTo("Solo run"));
        Assert.That(video.Description, Is.EqualTo("wind in face"));
        Assert.That(video.FileName, Is.EqualTo("run.mp4"));
        Assert.That(video.Location, Is.EqualTo("Kraków"));
        Assert.That(video.Category, Is.EqualTo(VideoCategories.Running));
        Assert.That(video.FileSizeBytes, Is.EqualTo(12_345));
        Assert.That(video.Media, Is.Empty);
        Assert.That(video.Latitude, Is.Null);
        Assert.That(video.Longitude, Is.Null);
        Assert.That(video.HasCoordinates, Is.False);
        Assert.That(video.HasTraining, Is.False);
        Assert.That(video.UploadedAt, Is.GreaterThanOrEqualTo(before));
        Assert.That(video.UploadedAt.Kind, Is.EqualTo(DateTimeKind.Utc));
    }

    [Test]
    public void HasCoordinates_RequiresBothLatAndLng()
    {
        var v = Video.Create(1, "t", "", "f.mp4", null, VideoCategories.Running, 0);
        Assert.That(v.HasCoordinates, Is.False);

        v.Latitude = 50.06;
        Assert.That(v.HasCoordinates, Is.False, "only lat is set");

        v.Longitude = 19.94;
        Assert.That(v.HasCoordinates, Is.True);
    }

    [Test]
    public void HasTraining_FlipsWhenTrainingAssigned()
    {
        var v = Video.Create(1, "t", "", "f.mp4", null, VideoCategories.Running, 0);
        Assert.That(v.HasTraining, Is.False);

        v.Training = new TrainingData { Source = TrainingSource.Strava, ExternalId = "1" };
        Assert.That(v.HasTraining, Is.True);
    }

    [Test]
    public void GetAllMedia_EmptyMedia_SynthesisesFromFileName()
    {
        var v = Video.Create(1, "t", "", "vid.MP4", null, VideoCategories.Running, 100);

        var media = v.GetAllMedia();

        Assert.That(media, Has.Count.EqualTo(1));
        Assert.That(media[0].FileName, Is.EqualTo("vid.MP4"));
        Assert.That(media[0].Type, Is.EqualTo(MediaType.Video));
        Assert.That(media[0].SizeBytes, Is.EqualTo(100));
        Assert.That(media[0].Order, Is.EqualTo(0));
    }

    [Test]
    public void GetAllMedia_EmptyMediaWithImageFile_SynthesisesImageType()
    {
        var v = Video.Create(1, "t", "", "cover.jpg", null, VideoCategories.Running, 100);

        var media = v.GetAllMedia();

        Assert.That(media[0].Type, Is.EqualTo(MediaType.Image));
    }

    [Test]
    public void GetAllMedia_MultipleMedia_OrderedByOrderAscending()
    {
        var v = Video.Create(1, "t", "", "a.mp4", null, VideoCategories.Running, 0);
        v.Media = new List<MediaItem>
        {
            MediaItem.Create("c.mp4", MediaType.Video, 30, order: 2),
            MediaItem.Create("a.mp4", MediaType.Video, 10, order: 0),
            MediaItem.Create("b.jpg", MediaType.Image, 20, order: 1)
        };

        var media = v.GetAllMedia();

        Assert.That(media.Select(m => m.Order).ToArray(), Is.EqualTo(new[] { 0, 1, 2 }));
        Assert.That(media[0].FileName, Is.EqualTo("a.mp4"));
        Assert.That(media[2].FileName, Is.EqualTo("c.mp4"));
    }
}

/// <summary>
/// Tests for the small <see cref="MediaItem"/> data record — primarily the
/// extension-to-type detector used when normalising legacy items.
/// </summary>
[TestFixture]
public sealed class MediaItemTests
{
    [TestCase("photo.jpg", MediaType.Image)]
    [TestCase("photo.JPG", MediaType.Image)]
    [TestCase("photo.jpeg", MediaType.Image)]
    [TestCase("art.PNG", MediaType.Image)]
    [TestCase("animated.gif", MediaType.Image)]
    [TestCase("modern.webp", MediaType.Image)]
    [TestCase("apple.heic", MediaType.Image)]
    [TestCase("clip.mp4", MediaType.Video)]
    [TestCase("clip.MOV", MediaType.Video)]
    [TestCase("clip.webm", MediaType.Video)]
    [TestCase("noextension", MediaType.Video)]
    public void DetectType_ReturnsExpectedTypeForExtension(string fileName, MediaType expected)
    {
        Assert.That(MediaItem.DetectType(fileName), Is.EqualTo(expected));
    }

    [Test]
    public void Create_PopulatesAllFields()
    {
        var item = MediaItem.Create("p.jpg", MediaType.Image, sizeBytes: 99, order: 3);

        Assert.That(item.FileName, Is.EqualTo("p.jpg"));
        Assert.That(item.Type, Is.EqualTo(MediaType.Image));
        Assert.That(item.SizeBytes, Is.EqualTo(99));
        Assert.That(item.Order, Is.EqualTo(3));
    }

    [Test]
    public void Create_DefaultOrderIsZero()
    {
        var item = MediaItem.Create("x.mp4", MediaType.Video, 0);
        Assert.That(item.Order, Is.EqualTo(0));
    }
}

/// <summary>
/// Tests for the <see cref="VideoCategories"/> static catalogue.
/// </summary>
[TestFixture]
public sealed class VideoCategoriesTests
{
    [Test]
    public void All_ContainsEverySixDefinedCategories()
    {
        Assert.That(VideoCategories.All, Has.Count.EqualTo(6));
        Assert.That(VideoCategories.All, Does.Contain(VideoCategories.Gory));
        Assert.That(VideoCategories.All, Does.Contain(VideoCategories.WspinaczkaSkalowa));
        Assert.That(VideoCategories.All, Does.Contain(VideoCategories.Bouldering));
        Assert.That(VideoCategories.All, Does.Contain(VideoCategories.ProwadzieniHala));
        Assert.That(VideoCategories.All, Does.Contain(VideoCategories.Calisthenics));
        Assert.That(VideoCategories.All, Does.Contain(VideoCategories.Running));
    }

    [TestCase("Mountains", "/gory")]
    [TestCase("Rock Climbing", "/wspinaczka-skalowa")]
    [TestCase("Bouldering", "/bouldering")]
    [TestCase("Indoor Climbing", "/prowadzeni-hala")]
    [TestCase("Calisthenics", "/calisthenics")]
    [TestCase("Running", "/running")]
    [TestCase("unknown", "/")]
    public void GetUrl_MapsCategoryToExpectedPath(string category, string expected)
    {
        Assert.That(VideoCategories.GetUrl(category), Is.EqualTo(expected));
    }

    [Test]
    public void GetPlaceholderImage_KnownCategory_ReturnsCategoryBgPath()
    {
        Assert.That(VideoCategories.GetPlaceholderImage(VideoCategories.Running),
            Is.EqualTo("/images/running-bg.jpg"));
        Assert.That(VideoCategories.GetPlaceholderImage(VideoCategories.Bouldering),
            Is.EqualTo("/images/bouldering-bg.jpg"));
    }

    [Test]
    public void GetPlaceholderImage_UnknownCategory_ReturnsFallback()
    {
        Assert.That(VideoCategories.GetPlaceholderImage("nope"),
            Is.EqualTo("/images/mountains-bg.jpg"));
    }
}
