using Microsoft.AspNetCore.Authentication.Cookies;
using MyHomePage.Abstractions;
using MyHomePage.Options;
using MyHomePage.Services;
using Xabe.FFmpeg;
using Xabe.FFmpeg.Downloader;

var builder = WebApplication.CreateBuilder(args);

// ── FFmpeg setup (auto-download on first run) ─────────────────────────────────
var ffmpegPath = Path.Combine(AppContext.BaseDirectory, "ffmpeg");
Directory.CreateDirectory(ffmpegPath);
FFmpeg.SetExecutablesPath(ffmpegPath);

var ffmpegExe = Path.Combine(ffmpegPath, OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");
if (!File.Exists(ffmpegExe))
{
    Console.WriteLine("Downloading FFmpeg (one-time setup)...");
    await FFmpegDownloader.GetLatestVersion(FFmpegVersion.Official, ffmpegPath);
    Console.WriteLine("FFmpeg ready.");
}

// ── Logging ───────────────────────────────────────────────────────────────────
builder.Services.AddLogging(logging =>
{
    logging.AddConsole();
    logging.SetMinimumLevel(LogLevel.Debug);
});

// ── Authentication ────────────────────────────────────────────────────────────
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.LogoutPath = "/logout";
        options.AccessDeniedPath = "/login";
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.SlidingExpiration = true;
    });

builder.Services.AddAuthorizationBuilder();
builder.Services.AddAntiforgery();
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();

// ── Options pattern ───────────────────────────────────────────────────────────
// Settings can be overridden in appsettings.json under "VideoStorage" section.
builder.Services.Configure<VideoStorageOptions>(
    builder.Configuration.GetSection(VideoStorageOptions.SectionName));

// ── Dependency injection — all registrations use interfaces (DIP) ─────────────

// File system operations
builder.Services.AddScoped<IFileStorageService, FileStorageService>();

// Compression strategy (Strategy pattern)
// To switch to a different codec, change H264CompressionStrategy → YourStrategy here.
builder.Services.AddScoped<ICompressionStrategy, H264CompressionStrategy>();

// Repository with Decorator pattern: JsonVideoRepository wrapped in LoggingVideoRepository
builder.Services.AddScoped<JsonVideoRepository>();
builder.Services.AddScoped<IVideoRepository>(sp =>
    new LoggingVideoRepository(
        sp.GetRequiredService<JsonVideoRepository>(),
        sp.GetRequiredService<ILogger<LoggingVideoRepository>>()));

// Application service
builder.Services.AddScoped<IVideoService, VideoService>();

// Credentials
builder.Services.AddScoped<ICredentialRepository, CredentialService>();

// ── Build & pipeline ──────────────────────────────────────────────────────────
var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapRazorPages();
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();
