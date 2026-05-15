namespace MyHomePage.Abstractions;

/// <summary>Decimal-degree GPS coordinates (WGS84).</summary>
public readonly record struct GeoCoordinates(double Latitude, double Longitude);

/// <summary>
/// Extracts GPS coordinates from media files (EXIF for images, QuickTime/MP4
/// metadata for videos). Returns null when no usable location data is present.
/// Interface Segregation Principle: callers only depend on this small contract.
/// </summary>
public interface ILocationExtractor
{
    /// <summary>Try to read GPS coords from the file at <paramref name="filePath"/>.</summary>
    GeoCoordinates? TryExtract(string filePath);
}
