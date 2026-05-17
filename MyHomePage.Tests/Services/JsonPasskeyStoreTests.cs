namespace MyHomePage.Tests.Services;

/// <summary>
/// End-to-end tests for <see cref="JsonPasskeyStore"/> against a temp directory.
/// Verifies that registered passkeys round-trip through disk and that the
/// lookup primitives (by email, by credential id, by user handle) return the
/// expected slices of the persisted set.
/// </summary>
[TestFixture]
public sealed class JsonPasskeyStoreTests
{
    private string _tempRoot = null!;
    private IFileStorageService _storage = null!;
    private JsonPasskeyStore _store = null!;

    [SetUp]
    public void Setup()
    {
        _tempRoot = Directory.CreateTempSubdirectory("passkey-").FullName;
        _storage = Substitute.For<IFileStorageService>();
        _storage.GetVideosRootPath().Returns(_tempRoot);

        var logger = Substitute.For<ILogger<JsonPasskeyStore>>();
        _store = new JsonPasskeyStore(_storage, logger);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    [Test]
    public async Task GetByEmail_NoFile_ReturnsEmpty()
    {
        var creds = await _store.GetByEmailAsync("nobody@example.com");

        Assert.That(creds, Is.Empty);
    }

    [Test]
    public async Task Add_PersistsAndRoundTripsByEmail()
    {
        var credential = BuildCredential(
            email: "Alice@Example.com",
            credentialId: "cred-1",
            userHandle: "handle-1");

        await _store.AddAsync(credential);
        var found = await _store.GetByEmailAsync("alice@example.com");

        Assert.That(found, Has.Count.EqualTo(1));
        Assert.That(found[0].CredentialId, Is.EqualTo("cred-1"));
        Assert.That(found[0].Nickname, Is.EqualTo("Windows Hello"));
    }

    [Test]
    public async Task Add_DuplicateCredentialId_Throws()
    {
        await _store.AddAsync(BuildCredential("alice@example.com", "cred-1", "handle-1"));

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _store.AddAsync(BuildCredential("bob@example.com", "cred-1", "handle-2")));
    }

    [Test]
    public async Task GetByCredentialId_ReturnsTheSingleMatch()
    {
        await _store.AddAsync(BuildCredential("alice@example.com", "cred-1", "handle-1"));
        await _store.AddAsync(BuildCredential("alice@example.com", "cred-2", "handle-1"));

        var match = await _store.GetByCredentialIdAsync("cred-2");

        Assert.That(match, Is.Not.Null);
        Assert.That(match!.UserEmail, Is.EqualTo("alice@example.com"));
    }

    [Test]
    public async Task GetByUserHandle_ReturnsAllCredentialsForThatHandle()
    {
        await _store.AddAsync(BuildCredential("alice@example.com", "cred-1", "handle-a"));
        await _store.AddAsync(BuildCredential("alice@example.com", "cred-2", "handle-a"));
        await _store.AddAsync(BuildCredential("bob@example.com", "cred-3", "handle-b"));

        var aliceCreds = await _store.GetByUserHandleAsync("handle-a");

        Assert.That(aliceCreds, Has.Count.EqualTo(2));
        Assert.That(aliceCreds.Select(c => c.CredentialId), Is.EquivalentTo(new[] { "cred-1", "cred-2" }));
    }

    [Test]
    public async Task Update_BumpsCounterAndLastUsed()
    {
        var initial = BuildCredential("alice@example.com", "cred-1", "handle-1", signatureCounter: 1);
        await _store.AddAsync(initial);

        var updated = initial with
        {
            SignatureCounter = 17,
            LastUsedAtUtc = new DateTimeOffset(2026, 5, 17, 12, 0, 0, TimeSpan.Zero),
        };
        await _store.UpdateAsync(updated);

        var reloaded = await _store.GetByCredentialIdAsync("cred-1");
        Assert.That(reloaded, Is.Not.Null);
        Assert.That(reloaded!.SignatureCounter, Is.EqualTo(17));
        Assert.That(reloaded.LastUsedAtUtc, Is.EqualTo(updated.LastUsedAtUtc));
    }

    [Test]
    public async Task Update_UnknownCredential_IsSilentNoOp()
    {
        var ghost = BuildCredential("alice@example.com", "missing", "handle-1");

        Assert.DoesNotThrowAsync(async () => await _store.UpdateAsync(ghost));
        var after = await _store.GetByCredentialIdAsync("missing");
        Assert.That(after, Is.Null);
    }

    [Test]
    public async Task Delete_RemovesOnlyTheMatchingCredential()
    {
        await _store.AddAsync(BuildCredential("alice@example.com", "cred-1", "handle-1"));
        await _store.AddAsync(BuildCredential("alice@example.com", "cred-2", "handle-1"));

        await _store.DeleteAsync("cred-1");

        var remaining = await _store.GetByEmailAsync("alice@example.com");
        Assert.That(remaining, Has.Count.EqualTo(1));
        Assert.That(remaining[0].CredentialId, Is.EqualTo("cred-2"));
    }

    [Test]
    public async Task Delete_UnknownCredentialId_IsSilentNoOp()
    {
        await _store.AddAsync(BuildCredential("alice@example.com", "cred-1", "handle-1"));

        Assert.DoesNotThrowAsync(async () => await _store.DeleteAsync("does-not-exist"));
        var remaining = await _store.GetByEmailAsync("alice@example.com");
        Assert.That(remaining, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task CorruptFile_IsTreatedAsEmpty()
    {
        await File.WriteAllTextAsync(
            Path.Combine(_tempRoot, "passkeys.json"),
            "{ this is not valid json");

        var creds = await _store.GetByEmailAsync("alice@example.com");

        Assert.That(creds, Is.Empty);
    }

    private static PasskeyCredential BuildCredential(
        string email,
        string credentialId,
        string userHandle,
        uint signatureCounter = 0) =>
        new(
            UserEmail: email,
            UserHandle: userHandle,
            CredentialId: credentialId,
            PublicKey: "fake-public-key",
            SignatureCounter: signatureCounter,
            AaGuid: Guid.Empty,
            Nickname: "Windows Hello",
            CreatedAtUtc: new DateTimeOffset(2026, 5, 17, 10, 0, 0, TimeSpan.Zero),
            LastUsedAtUtc: null);
}
