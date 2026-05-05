using Microsoft.AspNetCore.Authentication.Cookies;
using MyHomePage.Services;
using System.Security.Claims;
using Xabe.FFmpeg;
using Xabe.FFmpeg.Downloader;

var builder = WebApplication.CreateBuilder(args);

// Ensure FFmpeg is available (auto-download on first run)
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

builder.Services.AddLogging(logging =>
{
    logging.AddConsole();
    logging.SetMinimumLevel(LogLevel.Debug);
});

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
builder.Services.AddScoped<VideoService>();
builder.Services.AddScoped<CredentialService>();

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
