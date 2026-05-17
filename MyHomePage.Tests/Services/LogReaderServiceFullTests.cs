using System.Text.Json;

namespace MyHomePage.Tests.Services;

/// <summary>
/// End-to-end tests for <see cref="LogReaderService"/> against a temp logs
/// directory. The service is concerned with reading .clef files (one JSON
/// per line) and exposing entries newest-first, plus truncating files on
/// clear.
/// </summary>
[TestFixture]
public sealed class LogReaderServiceFullTests
{
    private string _tempContentRoot = null!;
    private string _logsDir = null!;
    private LogReaderService _service = null!;

    [SetUp]
    public void Setup()
    {
        _tempContentRoot = Directory.CreateTempSubdirectory("log-reader-").FullName;
        _logsDir = Path.Combine(_tempContentRoot, "logs");
        Directory.CreateDirectory(_logsDir);

        var env = new FakeEnv { ContentRootPath = _tempContentRoot };
        _service = new LogReaderService(env);
    }

    [TearDown]
    public void TearDown()
    {
        try { if (Directory.Exists(_tempContentRoot)) Directory.Delete(_tempContentRoot, true); }
        catch { /* best-effort */ }
    }

    [Test]
    public async Task GetEntriesAsync_NoLogsDirectory_ReturnsEmpty()
    {
        Directory.Delete(_logsDir, recursive: true);

        var entries = await _service.GetEntriesAsync();

        Assert.That(entries, Is.Empty);
    }

    [Test]
    public async Task GetEntriesAsync_NoFiles_ReturnsEmpty()
    {
        var entries = await _service.GetEntriesAsync();
        Assert.That(entries, Is.Empty);
    }

    [Test]
    public async Task GetEntriesAsync_SingleFile_ReturnsEntriesNewestFirst()
    {
        await WriteClef("today.clef",
            MakeClef("Login {User}", new { User = "alice" }, "Information"),
            MakeClef("Logout {User}", new { User = "alice" }, "Information"));

        var entries = await _service.GetEntriesAsync();

        Assert.That(entries, Has.Count.EqualTo(2));
        Assert.That(entries[0].MessageTemplate, Is.EqualTo("Logout {User}"));
        Assert.That(entries[1].MessageTemplate, Is.EqualTo("Login {User}"));
    }

    [Test]
    public async Task GetEntriesAsync_MultipleFiles_PrefersNewestFile()
    {
        var oldPath = await WriteClef("old.clef",
            MakeClef("Old line", null, "Information"));
        File.SetLastWriteTime(oldPath, DateTime.UtcNow.AddDays(-2));

        await WriteClef("new.clef",
            MakeClef("New line", null, "Information"));

        var entries = await _service.GetEntriesAsync();

        Assert.That(entries, Has.Count.EqualTo(2));
        Assert.That(entries[0].MessageTemplate, Is.EqualTo("New line"));
        Assert.That(entries[1].MessageTemplate, Is.EqualTo("Old line"));
    }

    [Test]
    public async Task GetEntriesAsync_HonoursMaxEntries()
    {
        var lines = Enumerable.Range(1, 10)
            .Select(i => MakeClef($"Line {i}", null, "Information"))
            .ToArray();
        await WriteClef("many.clef", lines);

        var entries = await _service.GetEntriesAsync(maxEntries: 3);

        Assert.That(entries, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task GetEntriesAsync_SkipsBlankLinesAndMalformedJson()
    {
        await File.WriteAllLinesAsync(Path.Combine(_logsDir, "mixed.clef"), new[]
        {
            MakeClef("Valid one", null, "Information"),
            "",                                    // blank
            "{ not valid json",                    // malformed
            "   ",                                 // whitespace
            MakeClef("Valid two", null, "Warning")
        });

        var entries = await _service.GetEntriesAsync();

        Assert.That(entries, Has.Count.EqualTo(2));
        Assert.That(entries.Select(e => e.MessageTemplate),
            Is.EquivalentTo(new[] { "Valid one", "Valid two" }));
    }

    [Test]
    public async Task ClearAsync_NoDirectory_IsNoOp()
    {
        Directory.Delete(_logsDir, recursive: true);

        Assert.DoesNotThrowAsync(async () => await _service.ClearAsync());
    }

    [Test]
    public async Task ClearAsync_TruncatesAllClefFiles()
    {
        await WriteClef("a.clef", MakeClef("line", null, "Information"));
        await WriteClef("b.clef", MakeClef("line", null, "Information"));
        var unrelated = Path.Combine(_logsDir, "notes.txt");
        await File.WriteAllTextAsync(unrelated, "do not touch");

        await _service.ClearAsync();

        Assert.That(new FileInfo(Path.Combine(_logsDir, "a.clef")).Length, Is.EqualTo(0));
        Assert.That(new FileInfo(Path.Combine(_logsDir, "b.clef")).Length, Is.EqualTo(0));
        Assert.That(await File.ReadAllTextAsync(unrelated), Is.EqualTo("do not touch"));
    }

    [Test]
    public void LogsDirectory_ReflectsContentRootPlusLogsFolder()
    {
        Assert.That(_service.LogsDirectory, Is.EqualTo(_logsDir));
    }

    // ── helpers ───────────────────────────────────────────────────────────

    private async Task<string> WriteClef(string fileName, params string[] lines)
    {
        var path = Path.Combine(_logsDir, fileName);
        await File.WriteAllLinesAsync(path, lines);
        return path;
    }

    private static string MakeClef(string template, object? props, string level)
    {
        var dict = new Dictionary<string, object?>
        {
            ["@t"] = DateTime.UtcNow,
            ["@mt"] = template,
            ["@l"] = level
        };
        if (props is not null)
        {
            foreach (var p in props.GetType().GetProperties())
                dict[p.Name] = p.GetValue(props);
        }
        return JsonSerializer.Serialize(dict);
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
