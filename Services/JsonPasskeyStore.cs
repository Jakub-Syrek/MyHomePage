using System.Text.Json;
using MyHomePage.Abstractions;
using MyHomePage.Models;

namespace MyHomePage.Services;

/// <summary>
/// Persists WebAuthn passkeys as a single JSON document under the configurable
/// storage root. Survives container restarts on Railway because the file lives
/// on the mounted volume alongside the Strava token file.
/// </summary>
public sealed class JsonPasskeyStore : IPasskeyStore
{
    private const string FileName = "passkeys.json";

    private static readonly JsonSerializerOptions JsonOptions =
        new() { WriteIndented = true };

    private static readonly SemaphoreSlim FileLock = new(1, 1);

    private readonly IFileStorageService _storage;
    private readonly ILogger<JsonPasskeyStore> _logger;

    /// <summary>Creates a new file-backed passkey store.</summary>
    /// <param name="storage">Resolves the absolute path of the storage root.</param>
    /// <param name="logger">Structured logger.</param>
    public JsonPasskeyStore(IFileStorageService storage, ILogger<JsonPasskeyStore> logger)
    {
        _storage = storage;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PasskeyCredential>> GetByEmailAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        var all = await LoadAllAsync(cancellationToken);
        return all
            .Where(c => string.Equals(c.UserEmail, email, StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => c.CreatedAtUtc)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<PasskeyCredential?> GetByCredentialIdAsync(
        string credentialId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(credentialId);
        var all = await LoadAllAsync(cancellationToken);
        return all.FirstOrDefault(c => c.CredentialId == credentialId);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PasskeyCredential>> GetByUserHandleAsync(
        string userHandle,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userHandle);
        var all = await LoadAllAsync(cancellationToken);
        return all.Where(c => c.UserHandle == userHandle).ToList();
    }

    /// <inheritdoc />
    public async Task AddAsync(
        PasskeyCredential credential,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credential);

        await FileLock.WaitAsync(cancellationToken);
        try
        {
            var all = await LoadAllUnlockedAsync(cancellationToken);
            if (all.Any(c => c.CredentialId == credential.CredentialId))
            {
                throw new InvalidOperationException(
                    $"Credential {credential.CredentialId} already registered.");
            }

            all.Add(credential);
            await SaveAllUnlockedAsync(all, cancellationToken);
            _logger.LogInformation(
                "Passkey registered for {Email} (credential {CredentialIdPrefix}...)",
                credential.UserEmail,
                credential.CredentialId[..Math.Min(12, credential.CredentialId.Length)]);
        }
        finally
        {
            FileLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task UpdateAsync(
        PasskeyCredential credential,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credential);

        await FileLock.WaitAsync(cancellationToken);
        try
        {
            var all = await LoadAllUnlockedAsync(cancellationToken);
            var index = all.FindIndex(c => c.CredentialId == credential.CredentialId);
            if (index < 0)
            {
                _logger.LogWarning(
                    "Update requested for unknown credential {CredentialIdPrefix}...",
                    credential.CredentialId[..Math.Min(12, credential.CredentialId.Length)]);
                return;
            }

            all[index] = credential;
            await SaveAllUnlockedAsync(all, cancellationToken);
        }
        finally
        {
            FileLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task DeleteAsync(
        string credentialId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(credentialId);

        await FileLock.WaitAsync(cancellationToken);
        try
        {
            var all = await LoadAllUnlockedAsync(cancellationToken);
            var removed = all.RemoveAll(c => c.CredentialId == credentialId);
            if (removed > 0)
            {
                await SaveAllUnlockedAsync(all, cancellationToken);
                _logger.LogInformation(
                    "Passkey {CredentialIdPrefix}... deleted",
                    credentialId[..Math.Min(12, credentialId.Length)]);
            }
        }
        finally
        {
            FileLock.Release();
        }
    }

    private async Task<List<PasskeyCredential>> LoadAllAsync(CancellationToken cancellationToken)
    {
        await FileLock.WaitAsync(cancellationToken);
        try
        {
            return await LoadAllUnlockedAsync(cancellationToken);
        }
        finally
        {
            FileLock.Release();
        }
    }

    private async Task<List<PasskeyCredential>> LoadAllUnlockedAsync(CancellationToken cancellationToken)
    {
        var path = ResolvePath();
        if (!File.Exists(path))
        {
            return new List<PasskeyCredential>();
        }

        try
        {
            await using var stream = File.OpenRead(path);
            var creds = await JsonSerializer.DeserializeAsync<List<PasskeyCredential>>(
                stream, JsonOptions, cancellationToken);
            return creds ?? new List<PasskeyCredential>();
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Passkey file at {Path} is corrupt; ignoring", path);
            return new List<PasskeyCredential>();
        }
    }

    private async Task SaveAllUnlockedAsync(
        List<PasskeyCredential> credentials,
        CancellationToken cancellationToken)
    {
        var path = ResolvePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var tempPath = path + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, credentials, JsonOptions, cancellationToken);
        }

        File.Move(tempPath, path, overwrite: true);
    }

    private string ResolvePath() =>
        Path.Combine(_storage.GetVideosRootPath(), FileName);
}
