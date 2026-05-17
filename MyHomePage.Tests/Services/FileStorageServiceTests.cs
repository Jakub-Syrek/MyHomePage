namespace MyHomePage.Tests.Services;

/// <summary>
/// Tests for <see cref="FileStorageService"/>. The service is exercised
/// against a temp directory used as the storage root so the path helpers
/// and copy logic can be verified end-to-end without mocking the
/// filesystem boundary. The env var <c>VIDEO_STORAGE_ROOT</c> is set per
/// fixture to override the resolution chain.
/// </summary>
[TestFixture]
public sealed class FileStorageServiceTests
{
    private string _tempStorageRoot = null!;
    private string _tempWebRoot = null!;
    private IWebHostEnvironment _env = null!;
    private FileStorageService _service = null!;
    private RecordingLogger _logger = null!;
    private string? _previousStorageRootEnv;

    [SetUp]
    public void Setup()
    {
        _tempStorageRoot = Directory.CreateTempSubdirectory("video-storage-").FullName;
        _tempWebRoot = Directory.CreateTempSubdirectory("video-webroot-").FullName;

        _previousStorageRootEnv = Environment.GetEnvironmentVariable("VIDEO_STORAGE_ROOT");
        Environment.SetEnvironmentVariable("VIDEO_STORAGE_ROOT", _tempStorageRoot);

        _env = new FakeWebHostEnvironment { WebRootPath = _tempWebRoot };

        var options = Microsoft.Extensions.Options.Options.Create(new VideoStorageOptions());
        _logger = new RecordingLogger();
        _service = new FileStorageService(_env, options, _logger);
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable("VIDEO_STORAGE_ROOT", _previousStorageRootEnv);
        TryDelete(_tempStorageRoot);
        TryDelete(_tempWebRoot);
    }

    [Test]
    public void GetVideosRootPath_ReturnsTheRootCreatedAtStartup()
    {
        Assert.That(_service.GetVideosRootPath(), Is.EqualTo(Path.GetFullPath(_tempStorageRoot)));
        Assert.That(Directory.Exists(_service.GetVideosRootPath()), Is.True);
    }

    [Test]
    public void GetVideoDirectoryPath_AppendsVideoIdAsFolder()
    {
        var dir = _service.GetVideoDirectoryPath(42);

        Assert.That(dir, Is.EqualTo(Path.Combine(_service.GetVideosRootPath(), "42")));
    }

    [Test]
    public void GetMetadataFilePath_ReturnsMetadataJsonInsideVideoFolder()
    {
        var path = _service.GetMetadataFilePath(7);

        Assert.That(path, Is.EqualTo(Path.Combine(_service.GetVideosRootPath(), "7", "metadata.json")));
    }

    [Test]
    public void EnsureVideoDirectoryExists_CreatesFolderIdempotently()
    {
        _service.EnsureVideoDirectoryExists(11);
        _service.EnsureVideoDirectoryExists(11); // second call should not throw

        Assert.That(Directory.Exists(_service.GetVideoDirectoryPath(11)), Is.True);
    }

    [Test]
    public void VideoDirectoryExists_FlipsWhenFolderIsCreated()
    {
        Assert.That(_service.VideoDirectoryExists(99), Is.False);

        _service.EnsureVideoDirectoryExists(99);

        Assert.That(_service.VideoDirectoryExists(99), Is.True);
    }

    [Test]
    public async Task DeleteVideoDirectoryAsync_RemovesFolderAndContents()
    {
        _service.EnsureVideoDirectoryExists(5);
        var inside = Path.Combine(_service.GetVideoDirectoryPath(5), "trash.txt");
        await File.WriteAllTextAsync(inside, "x");
        Assert.That(File.Exists(inside), Is.True);

        await _service.DeleteVideoDirectoryAsync(5);

        Assert.That(_service.VideoDirectoryExists(5), Is.False);
    }

    [Test]
    public async Task DeleteVideoDirectoryAsync_NoFolder_IsSilentNoOp()
    {
        Assert.DoesNotThrowAsync(async () => await _service.DeleteVideoDirectoryAsync(404));
    }

    [Test]
    public async Task CopyWwwRootFileToVideoAsync_CopiesAndReturnsByteCount()
    {
        var imagesDir = Path.Combine(_tempWebRoot, "images");
        Directory.CreateDirectory(imagesDir);
        var sourcePath = Path.Combine(imagesDir, "running-bg.jpg");
        var sourceBytes = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        await File.WriteAllBytesAsync(sourcePath, sourceBytes);

        var copied = await _service.CopyWwwRootFileToVideoAsync(
            "images/running-bg.jpg", videoId: 12, targetFileName: "cover.jpg");

        Assert.That(copied, Is.EqualTo(sourceBytes.Length));
        var destination = Path.Combine(_service.GetVideoDirectoryPath(12), "cover.jpg");
        Assert.That(File.Exists(destination), Is.True);
        Assert.That(await File.ReadAllBytesAsync(destination), Is.EqualTo(sourceBytes));
    }

    [Test]
    public async Task CopyWwwRootFileToVideoAsync_AcceptsBackslashAndLeadingSlash()
    {
        var imagesDir = Path.Combine(_tempWebRoot, "images");
        Directory.CreateDirectory(imagesDir);
        await File.WriteAllBytesAsync(
            Path.Combine(imagesDir, "bouldering-bg.jpg"),
            new byte[] { 9, 9, 9 });

        var copied = await _service.CopyWwwRootFileToVideoAsync(
            @"\images\bouldering-bg.jpg", videoId: 21, targetFileName: "cover.jpg");

        Assert.That(copied, Is.EqualTo(3));
    }

    [Test]
    public async Task CopyWwwRootFileToVideoAsync_SourceMissing_ReturnsZero()
    {
        var copied = await _service.CopyWwwRootFileToVideoAsync(
            "images/missing.jpg", videoId: 1, targetFileName: "cover.jpg");

        Assert.That(copied, Is.EqualTo(0));
        Assert.That(_service.VideoDirectoryExists(1), Is.False,
            "no directory should be created when the source is missing");
    }

    [Test]
    public async Task CopyWwwRootFileToVideoAsync_EmptyArguments_ReturnZeroWithoutTouchingDisk()
    {
        var a = await _service.CopyWwwRootFileToVideoAsync("", 1, "cover.jpg");
        var b = await _service.CopyWwwRootFileToVideoAsync("images/x.jpg", 1, "");

        Assert.That(a, Is.EqualTo(0));
        Assert.That(b, Is.EqualTo(0));
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch
        {
            // best-effort cleanup
        }
    }

    /// <summary>
    /// Plain implementation of <see cref="IWebHostEnvironment"/> so the
    /// service can read <c>WebRootPath</c> via direct property dispatch
    /// (NSubstitute is finicky with non-virtual property semantics on
    /// hosting interfaces).
    /// </summary>
    /// <summary>
    /// In-memory logger that records all log invocations so tests can
    /// inspect what the service emitted (and which paths it computed).
    /// </summary>
    private sealed class RecordingLogger : ILogger<FileStorageService>
    {
        public List<string> Messages { get; } = new();
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }

    private sealed class FakeWebHostEnvironment : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = string.Empty;
        public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = null!;
        public string ApplicationName { get; set; } = "MyHomePage.Tests";
        public string ContentRootPath { get; set; } = string.Empty;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
        public string EnvironmentName { get; set; } = "Testing";
    }
}
