namespace MyHomePage.Tests.Services;

[TestFixture]
public class VideoServiceTests
{
    private VideoService _service = null!;
    private IVideoRepository _mockRepository = null!;
    private IFileStorageService _mockFileStorage = null!;
    private ICompressionStrategy _mockCompression = null!;
    private ILogger<VideoService> _mockLogger = null!;
    private VideoStorageOptions _options = null!;

    [SetUp]
    public void Setup()
    {
        _mockRepository = Substitute.For<IVideoRepository>();
        _mockFileStorage = Substitute.For<IFileStorageService>();
        _mockCompression = Substitute.For<ICompressionStrategy>();
        _mockLogger = Substitute.For<ILogger<VideoService>>();
        _options = new VideoStorageOptions();

        _service = new VideoService(
            _mockRepository,
            _mockFileStorage,
            _mockCompression,
            Microsoft.Extensions.Options.Options.Create(_options),
            _mockLogger
        );
    }

    [Test]
    public async Task DeleteVideoAsync_VideoExists_ReturnsSuccess()
    {
        // Arrange
        _mockRepository.DeleteAsync(1).Returns(true);

        // Act
        var result = await _service.DeleteVideoAsync(1);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        await _mockRepository.Received(1).DeleteAsync(1);
    }

    [Test]
    public async Task DeleteVideoAsync_VideoNotFound_ReturnsFailure()
    {
        // Arrange
        _mockRepository.DeleteAsync(999).Returns(false);

        // Act
        var result = await _service.DeleteVideoAsync(999);

        // Assert
        Assert.That(result.IsSuccess, Is.False);
    }

    [Test]
    public async Task UpdateVideoAsync_VideoExists_UpdatesSuccessfully()
    {
        // Arrange
        var video = Video.Create(1, "Original", "Desc", "video.mp4", "Loc", "Mountains", 1000);
        _mockRepository.GetByIdAsync(1).Returns(video);
        _mockRepository.SaveAsync(Arg.Any<Video>()).Returns(Task.CompletedTask);

        // Act
        var result = await _service.UpdateVideoAsync(1, "Updated Title", "New Description", "New Location");

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        await _mockRepository.Received(1).SaveAsync(Arg.Any<Video>());
    }

    [Test]
    public async Task UpdateVideoAsync_VideoNotFound_ReturnsFailure()
    {
        // Arrange
        _mockRepository.GetByIdAsync(999).Returns((Video?)null);

        // Act
        var result = await _service.UpdateVideoAsync(999, "Title", "Description", "Location");

        // Assert
        Assert.That(result.IsSuccess, Is.False);
    }

    [Test]
    public async Task GetVideosByCategoryAsync_FilteringWorks()
    {
        // Arrange
        var videos = new[]
        {
            Video.Create(1, "Mountain Video", "Desc", "v1.mp4", "Alps", "Mountains", 1000),
            Video.Create(2, "Bouldering Video", "Desc", "v2.mp4", "Area", "Bouldering", 2000),
            Video.Create(3, "Another Mountain", "Desc", "v3.mp4", "Tatras", "Mountains", 3000)
        };
        _mockRepository.GetAllAsync().Returns(videos);

        // Act
        var result = await _service.GetVideosByCategoryAsync("Mountains");

        // Assert
        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result.All(v => v.Category == "Mountains"), Is.True);
    }

    [Test]
    public async Task GetAllVideosAsync_ReturnsAllVideos()
    {
        // Arrange
        var videos = new[]
        {
            Video.Create(1, "Video 1", "Desc 1", "v1.mp4", "Location 1", "Mountains", 1000),
            Video.Create(2, "Video 2", "Desc 2", "v2.mp4", "Location 2", "Bouldering", 2000)
        };
        _mockRepository.GetAllAsync().Returns(videos);

        // Act
        var result = await _service.GetAllVideosAsync();

        // Assert
        Assert.That(result, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task GetVideoByIdAsync_VideoExists_ReturnsVideo()
    {
        // Arrange
        var video = Video.Create(1, "Test Video", "Description", "video.mp4", "Location", "Mountains", 1000);
        _mockRepository.GetByIdAsync(1).Returns(video);

        // Act
        var result = await _service.GetVideoByIdAsync(1);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Id, Is.EqualTo(1));
        Assert.That(result.Title, Is.EqualTo("Test Video"));
    }

    [Test]
    public async Task GetVideoByIdAsync_VideoNotFound_ReturnsNull()
    {
        // Arrange
        _mockRepository.GetByIdAsync(999).Returns((Video?)null);

        // Act
        var result = await _service.GetVideoByIdAsync(999);

        // Assert
        Assert.That(result, Is.Null);
    }
}
