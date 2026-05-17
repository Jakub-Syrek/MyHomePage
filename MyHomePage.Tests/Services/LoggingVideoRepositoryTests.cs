namespace MyHomePage.Tests.Services;

/// <summary>
/// Unit tests for <see cref="LoggingVideoRepository"/>. The decorator must
/// be transparent: every call forwards to the inner repository unchanged,
/// the result is returned to the caller, and any thrown exception bubbles
/// out so error handling at higher layers still works.
/// </summary>
[TestFixture]
public sealed class LoggingVideoRepositoryTests
{
    private IVideoRepository _inner = null!;
    private LoggingVideoRepository _decorator = null!;

    [SetUp]
    public void Setup()
    {
        _inner = Substitute.For<IVideoRepository>();
        var logger = Substitute.For<ILogger<LoggingVideoRepository>>();
        _decorator = new LoggingVideoRepository(_inner, logger);
    }

    [Test]
    public async Task GetAllAsync_DelegatesAndReturnsInnerResult()
    {
        var expected = new[]
        {
            Video.Create(1, "A", "", "a.mp4", null, VideoCategories.Running, 0),
            Video.Create(2, "B", "", "b.mp4", null, VideoCategories.Running, 0)
        };
        _inner.GetAllAsync().Returns(expected);

        var result = await _decorator.GetAllAsync();

        Assert.That(result, Is.SameAs(expected));
        await _inner.Received(1).GetAllAsync();
    }

    [Test]
    public async Task GetByIdAsync_DelegatesWithSameId()
    {
        var expected = Video.Create(7, "T", "", "v.mp4", null, VideoCategories.Running, 0);
        _inner.GetByIdAsync(7).Returns(expected);

        var result = await _decorator.GetByIdAsync(7);

        Assert.That(result, Is.SameAs(expected));
        await _inner.Received(1).GetByIdAsync(7);
    }

    [Test]
    public async Task GetByIdAsync_NotFound_ReturnsNullFromInner()
    {
        _inner.GetByIdAsync(404).Returns((Video?)null);

        var result = await _decorator.GetByIdAsync(404);

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task SaveAsync_DelegatesSameInstance()
    {
        var video = Video.Create(11, "Save me", "", "s.mp4", null, VideoCategories.Running, 0);

        await _decorator.SaveAsync(video);

        await _inner.Received(1).SaveAsync(video);
    }

    [Test]
    public async Task DeleteAsync_ReturnsInnerBoolUnchanged()
    {
        _inner.DeleteAsync(5).Returns(true);
        _inner.DeleteAsync(6).Returns(false);

        Assert.That(await _decorator.DeleteAsync(5), Is.True);
        Assert.That(await _decorator.DeleteAsync(6), Is.False);
    }

    [Test]
    public void GenerateNextId_DelegatesToInner()
    {
        _inner.GenerateNextId().Returns(42);

        var id = _decorator.GenerateNextId();

        Assert.That(id, Is.EqualTo(42));
        _inner.Received(1).GenerateNextId();
    }

    [Test]
    public void SaveAsync_InnerThrows_ExceptionPropagates()
    {
        var video = Video.Create(1, "x", "", "x.mp4", null, VideoCategories.Running, 0);
        _inner.SaveAsync(video).Returns(Task.FromException(new IOException("disk full")));

        var ex = Assert.ThrowsAsync<IOException>(async () => await _decorator.SaveAsync(video));
        Assert.That(ex!.Message, Is.EqualTo("disk full"));
    }
}
