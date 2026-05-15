using System.IO.Compression;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
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

    // ── FFmpeg setup ──────────────────────────────────────────────────────────
    // Docker/Railway: FFMPEG_PATH=/usr/bin (apt-installed ffmpeg, no download)
    // Local dev: auto-download into ./ffmpeg on first run
    var ffmpegPath = Environment.GetEnvironmentVariable("FFMPEG_PATH")
                     ?? Path.Combine(AppContext.BaseDirectory, "ffmpeg");
    Directory.CreateDirectory(ffmpegPath);
    FFmpeg.SetExecutablesPath(ffmpegPath);

    var ffmpegExe = Path.Combine(ffmpegPath, OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");
    if (!File.Exists(ffmpegExe))
    {
        Log.Information("Downloading FFmpeg into {Path} (one-time setup)…", ffmpegPath);
        await FFmpegDownloader.GetLatestVersion(FFmpegVersion.Official, ffmpegPath);
        Log.Information("FFmpeg ready");
    }
    else
    {
        Log.Information("FFmpeg found at {Path}", ffmpegExe);
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

    // ── Response compression (Brotli + Gzip) ──────────────────────────────────
    // Compresses HTML/CSS/JS/JSON responses. NOT applied to MP4 (already compressed).
    builder.Services.AddResponseCompression(options =>
    {
        options.EnableForHttps = true;
        options.Providers.Add<BrotliCompressionProvider>();
        options.Providers.Add<GzipCompressionProvider>();
        options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(new[]
        {
            "application/octet-stream",
            "image/svg+xml"
        });
    });
    builder.Services.Configure<BrotliCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);
    builder.Services.Configure<GzipCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);

    // ── Larger upload limits for video files (5 GB) ───────────────────────────
    builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
    {
        options.MultipartBodyLengthLimit = 5L * 1024 * 1024 * 1024;
    });

    builder.WebHost.ConfigureKestrel(options =>
    {
        options.Limits.MaxRequestBodySize = 5L * 1024 * 1024 * 1024;
    });

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
        // Railway terminates TLS at the edge — don't redirect there
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RAILWAY_ENVIRONMENT")))
            app.UseHttpsRedirection();
    }

    app.UseResponseCompression();

    // ── Static files (wwwroot) with cache headers ─────────────────────────────
    var oneYear = TimeSpan.FromDays(365).TotalSeconds;
    var oneHour = TimeSpan.FromHours(1).TotalSeconds;
    app.UseStaticFiles(new StaticFileOptions
    {
        OnPrepareResponse = ctx =>
        {
            var path = ctx.File.Name.ToLowerInvariant();
            // Videos: 7-day immutable cache + range support
            if (path.EndsWith(".mp4") || path.EndsWith(".webm") ||
                path.EndsWith(".mov") || path.EndsWith(".m4v"))
            {
                ctx.Context.Response.Headers["Cache-Control"] = "public,max-age=604800,immutable";
                ctx.Context.Response.Headers["Accept-Ranges"] = "bytes";
            }
            // Long cache for hashed/static assets and images/fonts
            else if (path.EndsWith(".svg") || path.EndsWith(".woff2") || path.EndsWith(".woff") ||
                     path.EndsWith(".ttf") || path.EndsWith(".png") || path.EndsWith(".jpg") ||
                     path.EndsWith(".jpeg") || path.EndsWith(".webp"))
            {
                ctx.Context.Response.Headers["Cache-Control"] = $"public,max-age={oneYear},immutable";
            }
            else
            {
                // CSS/JS — short cache (Blazor regenerates these on deploy)
                ctx.Context.Response.Headers["Cache-Control"] = $"public,max-age={oneHour}";
            }
        }
    });

    // ── Serve videos from configurable storage root at /videos URL ────────────
    // Allows mounting a Railway volume at /data/videos outside the wwwroot folder.
    var fileStorage = app.Services.CreateScope().ServiceProvider.GetRequiredService<IFileStorageService>();
    var videosRoot = fileStorage.GetVideosRootPath();
    Directory.CreateDirectory(videosRoot);

    var videoMimeProvider = new FileExtensionContentTypeProvider();
    videoMimeProvider.Mappings[".mp4"] = "video/mp4";
    videoMimeProvider.Mappings[".webm"] = "video/webm";
    videoMimeProvider.Mappings[".mov"] = "video/quicktime";
    videoMimeProvider.Mappings[".m4v"] = "video/x-m4v";

    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(videosRoot),
        RequestPath = "/videos",
        ContentTypeProvider = videoMimeProvider,
        ServeUnknownFileTypes = false,
        OnPrepareResponse = ctx =>
        {
            var name = ctx.File.Name.ToLowerInvariant();
            if (name.EndsWith(".mp4") || name.EndsWith(".webm") ||
                name.EndsWith(".mov") || name.EndsWith(".m4v"))
            {
                // Videos: 7-day cache, immutable (videoId in path acts as cache buster)
                ctx.Context.Response.Headers["Cache-Control"] = "public,max-age=604800,immutable";
                ctx.Context.Response.Headers["Accept-Ranges"] = "bytes";
            }
            else
            {
                // metadata.json — no cache so edits are picked up immediately
                ctx.Context.Response.Headers["Cache-Control"] = "no-cache";
            }
        }
    });

    app.UseRouting();

    // ── HTTP request logging middleware ───────────────────────────────────────
    app.Use(async (context, next) =>
    {
        var request = context.Request;
        var method = request.Method;
        var path = request.Path.Value;
        var queryString = request.QueryString.Value;
        var remoteIp = context.Connection.RemoteIpAddress;

        Log.Information("HTTP {Method} {Path}{QueryString} from {IP}",
            method, path, queryString, remoteIp);

        var startTime = DateTime.UtcNow;
        try
        {
            await next();
            var duration = DateTime.UtcNow - startTime;
            var statusCode = context.Response.StatusCode;

            if (statusCode >= 400)
            {
                Log.Warning("HTTP {Method} {Path} returned {StatusCode} in {DurationMs}ms from {IP}",
                    method, path, statusCode, duration.TotalMilliseconds, remoteIp);
            }
            else
            {
                Log.Debug("HTTP {Method} {Path} returned {StatusCode} in {DurationMs}ms",
                    method, path, statusCode, duration.TotalMilliseconds);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "HTTP {Method} {Path} threw exception", method, path);
            throw;
        }
    });

    app.UseAuthentication();
    app.UseAuthorization();
    app.UseAntiforgery();

    // ── Health endpoint for Railway / monitoring ──────────────────────────────
    app.MapGet("/health", () => Results.Ok(new
    {
        status = "healthy",
        timestamp = DateTime.UtcNow,
        version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown"
    }));

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
