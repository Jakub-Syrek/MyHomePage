using Microsoft.AspNetCore.Components.Forms;

namespace MyHomePage.Tests.Services;

/// <summary>
/// Extended <see cref="VideoService"/> coverage — fills the gaps left by
/// the original happy-path suite. Focuses on validation, update-with-gps,
/// AppendMediaAsync and RemoveMediaAsync edge cases.
/// </summary>
[TestFixture]
public sealed class VideoServiceMoreTests
{
    private IVideoRepository _repo = null!;
    private IFileStorageService _storage = null!;
    private ICompressionStrategy _compression = null!;
    private ILocationExtractor _location = null!;
    private IDateTakenExtractor _dateExtractor = null!;
    private VideoStorageOptions _options = null!;
    private VideoService _service = null!;
    private string _tempDir = null!;

    [SetUp]
    public void Setup()
    {
        _repo = Substitute.For<IVideoRepository>();
        _storage = Substitute.For<IFileStorageService>();
        _compression = Substitute.For<ICompressionStrategy>();
        _location = Substitute.For<ILocationExtractor>();
        _dateExtractor = Substitute.For<IDateTakenExtractor>();
        _options = new VideoStorageOptions
        {
            MaxFileSizeBytes = 10 * 1024 * 1024,
            AllowedExtensions = new[] { ".mp4", ".mov", ".jpg", ".jpeg", ".png" }
        };

        _tempDir = Directory.CreateTempSubdirectory("video-svc-more-").FullName;
        _storage.GetVideoDirectoryPath(Arg.Any<int>())
            .Returns(call => Path.Combine(_tempDir, call.Arg<int>().ToString()));

        var logger = Substitute.For<ILogger<VideoService>>();
        _service = new VideoService(
            _repo, _storage, _compression, _location, _dateExtractor,
            Microsoft.Extensions.Options.Options.Create(_options), logger);
    }

    [TearDown]
    public void TearDown()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); }
        catch { }
    }

    // ── Upload validation ─────────────────────────────────────────────────

    [Test]
    public async Task UploadVideoAsync_NoFiles_ReturnsFailure()
    {
        var request = new VideoUploadRequest(
            Array.Empty<IBrowserFile>(), "T", "D", null, VideoCategories.Running);

        var result = await _service.UploadVideoAsync(request);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("No files"));
    }

    [Test]
    public async Task UploadVideoAsync_FileTooLarge_ReturnsFailure()
    {
        var file = Substitute.For<IBrowserFile>();
        file.Name.Returns("clip.mp4");
        file.Size.Returns(100L * 1024 * 1024); // 100 MB > 10 MB cap

        var request = new VideoUploadRequest(file, "T", "D", null, VideoCategories.Running);

        var result = await _service.UploadVideoAsync(request);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("too large"));
    }

    [Test]
    public async Task UploadVideoAsync_UnsupportedExtension_ReturnsFailure()
    {
        var file = Substitute.For<IBrowserFile>();
        file.Name.Returns("doc.exe");
        file.Size.Returns(100L);

        var request = new VideoUploadRequest(file, "T", "D", null, VideoCategories.Running);

        var result = await _service.UploadVideoAsync(request);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("Unsupported"));
    }

    // ── Update with GPS ───────────────────────────────────────────────────

    [Test]
    public async Task UpdateVideoAsync_WithExplicitCoordinates_PersistsThem()
    {
        var video = Video.Create(
            1, "old", "", "v.mp4", null, VideoCategories.Running, 0);
        _repo.GetByIdAsync(1).Returns(video);

        var result = await _service.UpdateVideoAsync(
            1, "new", "new desc", "Krakow",
            latitude: 50.06, longitude: 19.94);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(video.Latitude, Is.EqualTo(50.06));
        Assert.That(video.Longitude, Is.EqualTo(19.94));
        await _repo.Received(1).SaveAsync(video);
    }

    [Test]
    public async Task UpdateVideoAsync_OnlyLatitudeProvided_LeavesCoordsAsIs()
    {
        var video = Video.Create(
            1, "old", "", "v.mp4", null, VideoCategories.Running, 0);
        video.Latitude = 49.0;
        video.Longitude = 19.0;
        _repo.GetByIdAsync(1).Returns(video);

        var result = await _service.UpdateVideoAsync(
            1, "new", "", null, latitude: 50.06, longitude: null);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(video.Latitude, Is.EqualTo(49.0));
    }

    [Test]
    public async Task UpdateVideoAsync_RepositoryThrows_ReturnsFailure()
    {
        var video = Video.Create(
            1, "old", "", "v.mp4", null, VideoCategories.Running, 0);
        _repo.GetByIdAsync(1).Returns(video);
        _repo.SaveAsync(video).Returns(Task.FromException(new IOException("disk full")));

        var result = await _service.UpdateVideoAsync(
            1, "new", "", null, null, null);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("disk full"));
    }

    // ── DeleteVideoAsync error path ───────────────────────────────────────

    [Test]
    public async Task DeleteVideoAsync_RepositoryThrows_ReturnsFailure()
    {
        _repo.DeleteAsync(7).Returns(Task.FromException<bool>(
            new IOException("locked")));

        var result = await _service.DeleteVideoAsync(7);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("locked"));
    }

    // ── AppendMediaAsync ──────────────────────────────────────────────────

    [Test]
    public async Task AppendMediaAsync_EmptyList_ReturnsFailure()
    {
        var result = await _service.AppendMediaAsync(1, Array.Empty<IBrowserFile>());

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("No files"));
    }

    [Test]
    public async Task AppendMediaAsync_TooLarge_RejectedBeforeRepoLookup()
    {
        var file = Substitute.For<IBrowserFile>();
        file.Name.Returns("x.jpg");
        file.Size.Returns(50L * 1024 * 1024);

        var result = await _service.AppendMediaAsync(1, new[] { file });

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("too large"));
        await _repo.DidNotReceive().GetByIdAsync(Arg.Any<int>());
    }

    [Test]
    public async Task AppendMediaAsync_UnknownVideoId_ReturnsFailure()
    {
        var file = Substitute.For<IBrowserFile>();
        file.Name.Returns("photo.jpg");
        file.Size.Returns(1L);
        _repo.GetByIdAsync(404).Returns((Video?)null);

        var result = await _service.AppendMediaAsync(404, new[] { file });

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("not found"));
    }

    // ── RemoveMediaAsync ──────────────────────────────────────────────────

    [Test]
    public async Task RemoveMediaAsync_EmptyFileName_ReturnsFailure()
    {
        var result = await _service.RemoveMediaAsync(1, "");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("File name"));
    }

    [Test]
    public async Task RemoveMediaAsync_VideoNotFound_ReturnsFailure()
    {
        _repo.GetByIdAsync(404).Returns((Video?)null);

        var result = await _service.RemoveMediaAsync(404, "any.mp4");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("not found"));
    }

    [Test]
    public async Task RemoveMediaAsync_MediaNotInItem_ReturnsFailure()
    {
        var video = Video.Create(
            1, "T", "", "primary.mp4", null, VideoCategories.Running, 0);
        video.Media = new List<MediaItem>
        {
            MediaItem.Create("primary.mp4", MediaType.Video, 100, 0)
        };
        _repo.GetByIdAsync(1).Returns(video);

        var result = await _service.RemoveMediaAsync(1, "missing.jpg");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("not attached"));
    }

    [Test]
    public async Task RemoveMediaAsync_ExistingMediaWithFile_DeletesAndReorders()
    {
        var video = Video.Create(
            1, "T", "", "video.mp4", null, VideoCategories.Running, 300);
        video.Media = new List<MediaItem>
        {
            MediaItem.Create("video.mp4", MediaType.Video, 100, 0),
            MediaItem.Create("photo-01.jpg", MediaType.Image, 200, 1)
        };
        _repo.GetByIdAsync(1).Returns(video);

        // Pre-create the directory and the file we will remove.
        var dir = Path.Combine(_tempDir, "1");
        Directory.CreateDirectory(dir);
        var photoPath = Path.Combine(dir, "photo-01.jpg");
        await File.WriteAllBytesAsync(photoPath, new byte[200]);

        var result = await _service.RemoveMediaAsync(1, "photo-01.jpg");

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(video.Media, Has.Count.EqualTo(1));
        Assert.That(video.Media[0].FileName, Is.EqualTo("video.mp4"));
        Assert.That(video.Media[0].Order, Is.EqualTo(0));
        Assert.That(video.FileSizeBytes, Is.EqualTo(100));
        Assert.That(File.Exists(photoPath), Is.False);
        await _repo.Received(1).SaveAsync(video);
    }

    [Test]
    public async Task RemoveMediaAsync_RemovesPrimary_ReassignsToNextMedia()
    {
        var video = Video.Create(
            1, "T", "", "video.mp4", null, VideoCategories.Running, 300);
        video.Media = new List<MediaItem>
        {
            MediaItem.Create("video.mp4", MediaType.Video, 100, 0),
            MediaItem.Create("photo-01.jpg", MediaType.Image, 200, 1)
        };
        _repo.GetByIdAsync(1).Returns(video);

        // No physical file — service logs and continues.
        var result = await _service.RemoveMediaAsync(1, "video.mp4");

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(video.FileName, Is.EqualTo("photo-01.jpg"));
        Assert.That(video.Media, Has.Count.EqualTo(1));
        Assert.That(video.Media[0].Order, Is.EqualTo(0));
    }

    [Test]
    public async Task RemoveMediaAsync_RemovesLastMedia_RestoresCategoryPlaceholder()
    {
        var video = Video.Create(
            1, "T", "", "only.mp4", null, VideoCategories.Running, 100);
        video.Media = new List<MediaItem>
        {
            MediaItem.Create("only.mp4", MediaType.Video, 100, 0)
        };
        _repo.GetByIdAsync(1).Returns(video);
        // Pretend the wwwroot bg copy succeeds and returns a non-zero
        // byte count — that's how the service learns it can restore.
        _storage.CopyWwwRootFileToVideoAsync(
                Arg.Any<string>(), 1, "cover.jpg")
            .Returns(4242L);

        var result = await _service.RemoveMediaAsync(1, "only.mp4");

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Message, Does.Contain("Default"));
        Assert.That(video.Media, Has.Count.EqualTo(1));
        Assert.That(video.Media[0].FileName, Is.EqualTo("cover.jpg"));
        Assert.That(video.Media[0].Type, Is.EqualTo(MediaType.Image));
        Assert.That(video.Media[0].SizeBytes, Is.EqualTo(4242));
        Assert.That(video.FileName, Is.EqualTo("cover.jpg"));
        Assert.That(video.FileSizeBytes, Is.EqualTo(4242));
        await _storage.Received().CopyWwwRootFileToVideoAsync(
            Arg.Is<string>(p => p.Contains("running-bg")), 1, "cover.jpg");
        // og.jpg is re-rendered against the new cover.
        await _storage.Received().GenerateOgImageAsync(
            Arg.Is<string>(p => p.EndsWith("cover.jpg", StringComparison.OrdinalIgnoreCase)),
            Arg.Is<string>(p => p.EndsWith("og.jpg", StringComparison.OrdinalIgnoreCase)),
            Arg.Any<(double X, double Y)?>(),
            Arg.Any<OgOverlay?>());
    }

    [Test]
    public async Task RemoveMediaAsync_RemovesLastMedia_PlaceholderSeedFails_FallsBackToEmpty()
    {
        var video = Video.Create(
            1, "T", "", "only.mp4", null, VideoCategories.Running, 100);
        video.Media = new List<MediaItem>
        {
            MediaItem.Create("only.mp4", MediaType.Video, 100, 0)
        };
        _repo.GetByIdAsync(1).Returns(video);
        // No category bg asset on disk -> CopyWwwRootFileToVideoAsync returns 0.
        _storage.CopyWwwRootFileToVideoAsync(
                Arg.Any<string>(), 1, "cover.jpg")
            .Returns(0L);

        var result = await _service.RemoveMediaAsync(1, "only.mp4");

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(video.Media, Is.Empty);
        Assert.That(video.FileName, Is.EqualTo(string.Empty));
        Assert.That(video.FileSizeBytes, Is.EqualTo(0));
    }

    [Test]
    public async Task RemoveMediaAsync_RepoSaveThrows_ReturnsFailure()
    {
        var video = Video.Create(
            1, "T", "", "v.mp4", null, VideoCategories.Running, 100);
        video.Media = new List<MediaItem>
        {
            MediaItem.Create("v.mp4", MediaType.Video, 100, 0)
        };
        _repo.GetByIdAsync(1).Returns(video);
        _repo.SaveAsync(video).Returns(Task.FromException(new IOException("disk")));

        var result = await _service.RemoveMediaAsync(1, "v.mp4");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Message, Does.Contain("disk"));
    }
}
