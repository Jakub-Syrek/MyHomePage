namespace MyHomePage.Tests.Services;

[TestFixture]
public class LogReaderServiceTests
{
    private LogReaderService _service = null!;
    private IWebHostEnvironment _mockEnvironment = null!;

    [SetUp]
    public void Setup()
    {
        _mockEnvironment = Substitute.For<IWebHostEnvironment>();
        _mockEnvironment.ContentRootPath.Returns(Path.GetTempPath());
        _service = new LogReaderService(_mockEnvironment);
    }

    [Test]
    public async Task GetEntriesAsync_DirectoryDoesNotExist_ReturnsEmpty()
    {
        // Act
        var result = await _service.GetEntriesAsync();

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GetEntriesAsync_MaxEntries_RespectsLimit()
    {
        // Act
        var result = await _service.GetEntriesAsync(maxEntries: 10);

        // Assert
        Assert.That(result.Count, Is.LessThanOrEqualTo(10));
    }
}
