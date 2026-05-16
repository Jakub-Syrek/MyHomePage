namespace MyHomePage.Tests.Services;

/// <summary>
/// Unit tests for <see cref="PolylineDecoder"/>. Inputs are pinned against
/// the canonical example from Google's polyline algorithm documentation so
/// the implementation can be cross-checked at a glance.
/// </summary>
[TestFixture]
public sealed class PolylineDecoderTests
{
    /// <summary>
    /// Canonical sample from
    /// https://developers.google.com/maps/documentation/utilities/polylinealgorithm
    /// — decodes to three points in California.
    /// </summary>
    private const string GoogleSampleEncoded = "_p~iF~ps|U_ulLnnqC_mqNvxq`@";

    [Test]
    public void Decode_GoogleSample_ReturnsThreePoints()
    {
        var points = PolylineDecoder.Decode(GoogleSampleEncoded);

        Assert.That(points, Has.Count.EqualTo(3));
        Assert.That(points[0].Latitude, Is.EqualTo(38.5).Within(0.001));
        Assert.That(points[0].Longitude, Is.EqualTo(-120.2).Within(0.001));
        Assert.That(points[1].Latitude, Is.EqualTo(40.7).Within(0.001));
        Assert.That(points[1].Longitude, Is.EqualTo(-120.95).Within(0.001));
        Assert.That(points[2].Latitude, Is.EqualTo(43.252).Within(0.001));
        Assert.That(points[2].Longitude, Is.EqualTo(-126.453).Within(0.001));
    }

    [Test]
    public void FirstPoint_GoogleSample_ReturnsFirstCoordinate()
    {
        var first = PolylineDecoder.FirstPoint(GoogleSampleEncoded);

        Assert.That(first, Is.Not.Null);
        Assert.That(first!.Value.Latitude, Is.EqualTo(38.5).Within(0.001));
        Assert.That(first.Value.Longitude, Is.EqualTo(-120.2).Within(0.001));
    }

    [TestCase(null)]
    [TestCase("")]
    public void Decode_NullOrEmpty_ReturnsEmptyList(string? encoded)
    {
        var points = PolylineDecoder.Decode(encoded);

        Assert.That(points, Is.Empty);
    }

    [TestCase(null)]
    [TestCase("")]
    public void FirstPoint_NullOrEmpty_ReturnsNull(string? encoded)
    {
        var first = PolylineDecoder.FirstPoint(encoded);

        Assert.That(first, Is.Null);
    }
}
