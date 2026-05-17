namespace MyHomePage.Tests.Models;

/// <summary>
/// Locks in the friendly labels surfaced in /settings/passkeys so a future
/// refactor of the transport-to-label mapping does not silently regress the
/// user-facing strings.
/// </summary>
[TestFixture]
public sealed class PasskeyTypeFormatterTests
{
    [Test]
    public void Describe_NullOrEmpty_ReturnsUnknown()
    {
        Assert.That(PasskeyTypeFormatter.Describe(null), Is.EqualTo("Unknown"));
        Assert.That(PasskeyTypeFormatter.Describe(Array.Empty<string>()), Is.EqualTo("Unknown"));
    }

    [Test]
    public void Describe_Internal_IsFingerprint() =>
        Assert.That(
            PasskeyTypeFormatter.Describe(new[] { "Internal" }),
            Is.EqualTo("Fingerprint / face / PIN"));

    [Test]
    public void Describe_InternalAndHybrid_PrefersInternal() =>
        Assert.That(
            PasskeyTypeFormatter.Describe(new[] { "Hybrid", "Internal" }),
            Is.EqualTo("Fingerprint / face / PIN"));

    [Test]
    public void Describe_Hybrid_IsPhone() =>
        Assert.That(
            PasskeyTypeFormatter.Describe(new[] { "Hybrid" }),
            Is.EqualTo("Phone / cross-device"));

    [Test]
    public void Describe_Usb_IsSecurityKeyUsb() =>
        Assert.That(
            PasskeyTypeFormatter.Describe(new[] { "Usb" }),
            Is.EqualTo("Security key (USB)"));

    [Test]
    public void Describe_UsbNfcBle_LabelsAllTransports() =>
        Assert.That(
            PasskeyTypeFormatter.Describe(new[] { "Usb", "Nfc", "Ble" }),
            Is.EqualTo("Security key (USB / NFC / Bluetooth)"));

    [Test]
    public void Describe_IsCaseInsensitive() =>
        Assert.That(
            PasskeyTypeFormatter.Describe(new[] { "internal" }),
            Is.EqualTo("Fingerprint / face / PIN"));

    [Test]
    public void Describe_IgnoresUnknownTransports() =>
        Assert.That(
            PasskeyTypeFormatter.Describe(new[] { "WeirdNewTransport" }),
            Is.EqualTo("Unknown"));
}
