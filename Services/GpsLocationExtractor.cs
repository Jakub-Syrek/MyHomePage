using System.Globalization;
using System.Text.RegularExpressions;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.QuickTime;
using MyHomePage.Abstractions;

namespace MyHomePage.Services;

/// <summary>
/// Reads GPS coordinates from media metadata:
///   * EXIF (JPEG/HEIC) — GpsDirectory
///   * QuickTime (MP4/MOV) — com.apple.quicktime.location.ISO6709 / Xyz tags
///
/// Best-effort: silently returns null if no usable data is present.
/// </summary>
public sealed class GpsLocationExtractor : ILocationExtractor
{
    private readonly ILogger<GpsLocationExtractor> _logger;

    // ISO 6109: ±DD.DDDD±DDD.DDDD/  or  ±DDMM.MMM±DDDMM.MM/ etc.
    private static readonly Regex Iso6709 = new(
        @"^([+-]\d{2,3}(?:\.\d+)?)([+-]\d{2,3}(?:\.\d+)?)",
        RegexOptions.Compiled);

    public GpsLocationExtractor(ILogger<GpsLocationExtractor> logger)
    {
        _logger = logger;
    }

    public GeoCoordinates? TryExtract(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return null;

            IReadOnlyList<MetadataExtractor.Directory> dirs;
            try
            {
                dirs = ImageMetadataReader.ReadMetadata(filePath);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not read metadata for {File}", filePath);
                return null;
            }

            // 1. JPEG / HEIC — standard EXIF GPS directory
            var gps = dirs.OfType<GpsDirectory>().FirstOrDefault();
            var location = gps?.GetGeoLocation();
            if (location is not null)
            {
                _logger.LogInformation(
                    "GPS from EXIF for {File}: {Lat}, {Lon}",
                    Path.GetFileName(filePath), location.Latitude, location.Longitude);
                return new GeoCoordinates(location.Latitude, location.Longitude);
            }

            // 2. MP4 / MOV — QuickTime metadata header (Apple ISO6709 string)
            var qtMeta = dirs.OfType<QuickTimeMetadataHeaderDirectory>().FirstOrDefault();
            if (qtMeta is not null)
            {
                foreach (var tag in qtMeta.Tags)
                {
                    if (tag.Name is null) continue;
                    if (!tag.Name.Contains("location", StringComparison.OrdinalIgnoreCase)) continue;

                    var raw = qtMeta.GetString(tag.Type);
                    if (TryParseIso6709(raw, out var qtCoords))
                    {
                        _logger.LogInformation(
                            "GPS from QuickTime metadata for {File}: {Lat}, {Lon}",
                            Path.GetFileName(filePath), qtCoords.Latitude, qtCoords.Longitude);
                        return qtCoords;
                    }
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract GPS from {File}", filePath);
            return null;
        }
    }

    internal static bool TryParseIso6709(string? raw, out GeoCoordinates coords)
    {
        coords = default;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        var match = Iso6709.Match(raw);
        if (!match.Success) return false;

        if (double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) &&
            double.TryParse(match.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var lon))
        {
            coords = new GeoCoordinates(lat, lon);
            return true;
        }
        return false;
    }
}
