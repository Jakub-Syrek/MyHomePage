using Microsoft.AspNetCore.Authentication.Cookies;
using MyHomePage.Abstractions;
using MyHomePage.Options;
using MyHomePage.Services;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;
using Xabe.FFmpeg;
using Xabe.FFmpeg.Downloader;

// ── Serilog: configure before the host so startup errors are captured ─────────
var logsPath = Path.Combine(AppContext.BaseDirectory, "logs", "app-.clef");
Directory.CreateDirectory(Path.GetDirectoryName(logsPath)!);

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("System", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext:l} — {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(
        formatter: new CompactJsonFormatter(),
        path: logsPath,
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        shared: true)
    .CreateLogger();

try
{
    Log.Information("=== My Mountain Adventures starting up ===");

    var builder = WebApplication.CreateBuilder(args);
    builder.Host.UseSerilog();

    // ── FFmpeg setup (auto-download on first run) ─────────────────────────────
    var ffmpegPath = Path.Combine(AppContext.BaseDirectory, "ffmpeg");
    Directory.CreateDirectory(ffmpegPath);
    FFmpeg.SetExecutablesPath(ffmpegPath);

    var ffmpegExe = Path.Combine(ffmpegPath, OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");
    if (!File.Exists(ffmpegExe))
    {
        Log.Information("Downloading FFmpeg (one-time setup)…");
        await FFmpegDownloader.GetLatestVersion(FFmpegVersion.Official, ffmpegPath);
        Log.Information("FFmpeg ready");
    }

    // ── Authentication ────────────────────────────────────────────────────────
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

    // ── Options pattern ───────────────────────────────────────────────────────
    builder.Services.Configure<VideoStorageOptions>(
        builder.Configuration.GetSection(VideoStorageOptions.SectionName));

    // ── Dependency injection ──────────────────────────────────────────────────
    builder.Services.AddScoped<IFileStorageService, FileStorageService>();
    builder.Services.AddScoped<ICompressionStrategy, H264CompressionStrategy>();

    // Repository + Decorator
    builder.Services.AddScoped<JsonVideoRepository>();
    builder.Services.AddScoped<IVideoRepository>(sp =>
        new LoggingVideoRepository(
            sp.GetRequiredService<JsonVideoRepository>(),
            sp.GetRequiredService<ILogger<LoggingVideoRepository>>()));

    builder.Services.AddScoped<IVideoService, VideoService>();
    builder.Services.AddScoped<ICredentialRepository, CredentialService>();
    builder.Services.AddScoped<ILogReaderService, LogReaderService>();

    // ── Build & pipeline ──────────────────────────────────────────────────────
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

    Log.Information("Application pipeline built — listening for requests");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.Information("=== My Mountain Adventures shutting down ===");
    await Log.CloseAndFlushAsync();
}
