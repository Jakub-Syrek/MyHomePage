using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace MyHomePage.Tests.Services;

/// <summary>
/// Tests for <see cref="FileStorageService.GenerateOgImageAsync"/>. The
/// cropping is done with ImageSharp on real bytes, so each test generates
/// a tiny synthetic JPEG on disk and asserts the output's dimensions +
/// pixel colour at known coordinates — that's how we prove the focal
/// point landed where the contract says it should.
/// </summary>
[TestFixture]
public sealed class FileStorageServiceOgTests
{
    private string _tempStorageRoot = null!;
    private string _tempWebRoot = null!;
    private FileStorageService _service = null!;
    private string? _previousStorageRootEnv;

    [SetUp]
    public void Setup()
    {
        _tempStorageRoot = Directory.CreateTempSubdirectory("og-storage-").FullName;
        _tempWebRoot = Directory.CreateTempSubdirectory("og-webroot-").FullName;

        _previousStorageRootEnv = Environment.GetEnvironmentVariable("VIDEO_STORAGE_ROOT");
        Environment.SetEnvironmentVariable("VIDEO_STORAGE_ROOT", _tempStorageRoot);

        var env = new FakeEnv { WebRootPath = _tempWebRoot };
        var options = Microsoft.Extensions.Options.Options.Create(new VideoStorageOptions());
        var logger = Substitute.For<ILogger<FileStorageService>>();
        _service = new FileStorageService(env, options, logger);
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable("VIDEO_STORAGE_ROOT", _previousStorageRootEnv);
        TryDelete(_tempStorageRoot);
        TryDelete(_tempWebRoot);
    }

    [Test]
    public async Task GenerateOgImageAsync_LandscapeSource_ProducesExactly1200x630()
    {
        var sourcePath = Path.Combine(_tempStorageRoot, "landscape.jpg");
        await WriteSolidJpegAsync(sourcePath, width: 4000, height: 3000, color: Color.SteelBlue);

        var ogPath = Path.Combine(_tempStorageRoot, "og.jpg");
        var size = await _service.GenerateOgImageAsync(sourcePath, ogPath);

        Assert.That(size, Is.GreaterThan(0));
        using var image = await Image.LoadAsync(ogPath);
        Assert.That(image.Width, Is.EqualTo(1200));
        Assert.That(image.Height, Is.EqualTo(630));
    }

    [Test]
    public async Task GenerateOgImageAsync_PortraitSource_ProducesExactly1200x630()
    {
        // A 3:4 portrait should be cropped to 1.91:1 (vertical bars trimmed).
        var sourcePath = Path.Combine(_tempStorageRoot, "portrait.jpg");
        await WriteSolidJpegAsync(sourcePath, width: 1200, height: 1600, color: Color.Orange);

        var ogPath = Path.Combine(_tempStorageRoot, "og.jpg");
        var size = await _service.GenerateOgImageAsync(sourcePath, ogPath);

        Assert.That(size, Is.GreaterThan(0));
        using var image = await Image.LoadAsync(ogPath);
        Assert.That(image.Width, Is.EqualTo(1200));
        Assert.That(image.Height, Is.EqualTo(630));
    }

    [Test]
    public async Task GenerateOgImageAsync_MissingSource_ReturnsZeroWithoutThrowing()
    {
        var ogPath = Path.Combine(_tempStorageRoot, "og.jpg");

        var size = await _service.GenerateOgImageAsync(
            Path.Combine(_tempStorageRoot, "does-not-exist.jpg"), ogPath);

        Assert.That(size, Is.EqualTo(0));
        Assert.That(File.Exists(ogPath), Is.False);
    }

    [Test]
    public async Task GenerateOgImageAsync_FocusOnLeftBand_KeepsLeftPixels()
    {
        // Build a 4000x1000 source with a red left half and a blue right
        // half. A centre crop would dilute both; focusing on the left
        // band should give us an OG image that is dominated by red.
        var sourcePath = Path.Combine(_tempStorageRoot, "split.jpg");
        await WriteHorizontallySplitJpegAsync(
            sourcePath, width: 4000, height: 1000, left: Color.Red, right: Color.Blue);

        var ogPath = Path.Combine(_tempStorageRoot, "og.jpg");
        // Focus at 10 % from left, vertical centre.
        await _service.GenerateOgImageAsync(sourcePath, ogPath, cropFocus: (0.1, 0.5));

        using var image = await Image.LoadAsync<Rgba32>(ogPath);
        var leftSample = image[10, image.Height / 2];
        var rightSample = image[image.Width - 10, image.Height / 2];

        // Both samples should be red-dominant because the crop window
        // was shifted left and the right half of the source never lands
        // in the OG output. JPEG compression + resize fuzz the exact
        // RGB values so we test the dominant channel rather than equality.
        Assert.That(leftSample.R, Is.GreaterThan(leftSample.B + 50),
            "left sample of the OG image should be red-dominant");
        Assert.That(rightSample.R, Is.GreaterThan(rightSample.B + 50),
            "right sample of the OG image should also be red-dominant when focus is on the left band");
    }

    [Test]
    public async Task GenerateOgImageAsync_FocusOnRightBand_KeepsRightPixels()
    {
        var sourcePath = Path.Combine(_tempStorageRoot, "split.jpg");
        await WriteHorizontallySplitJpegAsync(
            sourcePath, width: 4000, height: 1000, left: Color.Red, right: Color.Blue);

        var ogPath = Path.Combine(_tempStorageRoot, "og.jpg");
        await _service.GenerateOgImageAsync(sourcePath, ogPath, cropFocus: (0.9, 0.5));

        using var image = await Image.LoadAsync<Rgba32>(ogPath);
        var leftSample = image[10, image.Height / 2];
        var rightSample = image[image.Width - 10, image.Height / 2];

        Assert.That(leftSample.B, Is.GreaterThan(leftSample.R + 50));
        Assert.That(rightSample.B, Is.GreaterThan(rightSample.R + 50));
    }

    [Test]
    public async Task GenerateOgImageAsync_FocusOutOfRange_ClampedAndStillProduces1200x630()
    {
        var sourcePath = Path.Combine(_tempStorageRoot, "landscape.jpg");
        await WriteSolidJpegAsync(sourcePath, width: 3000, height: 2000, color: Color.Green);

        var ogPath = Path.Combine(_tempStorageRoot, "og.jpg");
        var size = await _service.GenerateOgImageAsync(sourcePath, ogPath, cropFocus: (5.0, -1.0));

        Assert.That(size, Is.GreaterThan(0));
        using var image = await Image.LoadAsync(ogPath);
        Assert.That(image.Width, Is.EqualTo(1200));
        Assert.That(image.Height, Is.EqualTo(630));
    }

    // ── helpers ───────────────────────────────────────────────────────────

    private static async Task WriteSolidJpegAsync(string path, int width, int height, Color color)
    {
        using var img = new Image<Rgba32>(width, height, color);
        await img.SaveAsJpegAsync(path);
    }

    private static async Task WriteHorizontallySplitJpegAsync(
        string path, int width, int height, Color left, Color right)
    {
        // Direct pixel-row fill — avoids taking a dependency on
        // SixLabors.ImageSharp.Drawing just for one test fixture.
        var leftPx = left.ToPixel<Rgba32>();
        var rightPx = right.ToPixel<Rgba32>();
        using var img = new Image<Rgba32>(width, height);
        var mid = width / 2;
        img.ProcessPixelRows(rows =>
        {
            for (var y = 0; y < rows.Height; y++)
            {
                var row = rows.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                    row[x] = x < mid ? leftPx : rightPx;
            }
        });
        await img.SaveAsJpegAsync(path);
    }

    private static void TryDelete(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    private sealed class FakeEnv : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = string.Empty;
        public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = null!;
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = string.Empty;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
        public string EnvironmentName { get; set; } = "Testing";
    }
}
