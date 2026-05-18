using System.Text.Json;
using MyHomePage.Abstractions;

namespace MyHomePage.Services;

/// <summary>
/// File-backed implementation of <see cref="IPasswordResetTokenStore"/>.
/// Persists tokens as a single JSON array alongside the Strava token
/// and passkey files under the configurable storage root so they
/// survive container restarts on Railway. The file is rewritten in
/// full on every mutation — fine for the expected volume (a handful
/// of tokens, never simultaneous).
/// </summary>
public sealed class JsonPasswordResetTokenStore : IPasswordResetTokenStore
{
    private const string FileName = "password-reset-tokens.json";

    private static readonly JsonSerializerOptions JsonOptions =
        new() { WriteIndented = true };

    private static readonly SemaphoreSlim FileLock = new(1, 1);

    private readonly IFileStorageService _storage;
    private readonly ILogger<JsonPasswordResetTokenStore> _logger;

    public JsonPasswordResetTokenStore(
        IFileStorageService storage,
        ILogger<JsonPasswordResetTokenStore> logger)
    {
        _storage = storage;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task SaveAsync(PasswordResetToken token, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(token);

        await FileLock.WaitAsync(cancellationToken);
        try
        {
            var all = await LoadAllAsync(cancellationToken);

            // Opportunistic cleanup — drop everything that has expired
            // more than 24 h ago so the file doesn't grow forever.
            var cutoff = DateTime.UtcNow.AddDays(-1);
            all.RemoveAll(t => t.ExpiresUtc < cutoff);

            all.Add(token);
            await WriteAllAsync(all, cancellationToken);

            _logger.LogInformation(
                "Password-reset token issued for {Email}, expires at {Expires}",
                token.Email, token.ExpiresUtc);
        }
        finally
        {
            FileLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<PasswordResetToken?> FindActiveAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);

        await FileLock.WaitAsync(cancellationToken);
        try
        {
            var all = await LoadAllAsync(cancellationToken);
            var now = DateTime.UtcNow;
            return all.FirstOrDefault(t =>
                t.TokenHash == tokenHash
                && t.UsedUtc is null
                && t.ExpiresUtc > now);
        }
        finally
        {
            FileLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task MarkUsedAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);

        await FileLock.WaitAsync(cancellationToken);
        try
        {
            var all = await LoadAllAsync(cancellationToken);
            var index = all.FindIndex(t => t.TokenHash == tokenHash);
            if (index < 0)
            {
                _logger.LogWarning(
                    "Tried to mark unknown token hash as used: {Hash}",
                    tokenHash[..Math.Min(8, tokenHash.Length)] + "…");
                return;
            }

            all[index] = all[index] with { UsedUtc = DateTime.UtcNow };
            await WriteAllAsync(all, cancellationToken);
            _logger.LogInformation(
                "Password-reset token consumed for {Email}",
                all[index].Email);
        }
        finally
        {
            FileLock.Release();
        }
    }

    private string ResolvePath() =>
        Path.Combine(_storage.GetVideosRootPath(), FileName);

    private async Task<List<PasswordResetToken>> LoadAllAsync(CancellationToken cancellationToken)
    {
        var path = ResolvePath();
        if (!File.Exists(path)) return new List<PasswordResetToken>();
        try
        {
            await using var stream = File.OpenRead(path);
            var loaded = await JsonSerializer.DeserializeAsync<List<PasswordResetToken>>(
                stream, JsonOptions, cancellationToken);
            return loaded ?? new List<PasswordResetToken>();
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex,
                "Password-reset token file at {Path} is corrupt — starting fresh",
                path);
            return new List<PasswordResetToken>();
        }
    }

    private async Task WriteAllAsync(List<PasswordResetToken> tokens, CancellationToken cancellationToken)
    {
        var path = ResolvePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, tokens, JsonOptions, cancellationToken);
    }
}
