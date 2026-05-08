namespace MyHomePage.Tests.Repositories;

[TestFixture]
public class JsonVideoRepositoryTests
{
    private JsonVideoRepository _repository = null!;
    private IFileStorageService _mockFileStorage = null!;

    [SetUp]
    public void Setup()
    {
        _mockFileStorage = Substitute.For<IFileStorageService>();
        _repository = new JsonVideoRepository(_mockFileStorage);
    }

    [Test]
    public async Task GetAllAsync_NoVideos_ReturnsEmpty()
    {
        // Arrange
        _mockFileStorage.GetVideosRootPath().Returns("/nonexistent");

        // Act
        var videos = await _repository.GetAllAsync();

        // Assert
        Assert.That(videos, Is.Empty);
    }

    [Test]
    public async Task GetByIdAsync_VideoNotExists_ReturnsNull()
    {
        // Arrange - no setup needed, mock will return defaults

        // Act
        var retrieved = await _repository.GetByIdAsync(999);

        // Assert
        Assert.That(retrieved, Is.Null);
    }

    [Test]
    public void GenerateNextId_ReturnsValidId()
    {
        // Act
        var id = _repository.GenerateNextId();

        // Assert
        Assert.That(id, Is.GreaterThan(0));
    }
}
