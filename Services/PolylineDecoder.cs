namespace MyHomePage.Services;

/// <summary>
/// Decoder for Google's Encoded Polyline Algorithm (precision 5), the same
/// format Strava emits in <c>map.summary_polyline</c> and <c>map.polyline</c>
/// for every GPS-tracked activity.
///
/// Reference: https://developers.google.com/maps/documentation/utilities/polylinealgorithm
///
/// Pure, stateless and allocation-light so it can be called from the import
/// pipeline on every activity without measurable cost.
/// </summary>
public static class PolylineDecoder
{
    /// <summary>Default precision used by Strava (5 decimal places).</summary>
    private const int DefaultPrecision = 5;

    /// <summary>
    /// Decodes an encoded polyline string into an ordered list of GPS points.
    /// Returns an empty list when the input is null / empty / malformed.
    /// </summary>
    /// <param name="encoded">Encoded polyline string as produced by Google's algorithm.</param>
    /// <param name="precision">Decimal precision used during encoding. Defaults to 5 (Strava).</param>
    public static IReadOnlyList<(double Latitude, double Longitude)> Decode(
        string? encoded,
        int precision = DefaultPrecision)
    {
        if (string.IsNullOrEmpty(encoded)) return Array.Empty<(double, double)>();

        var factor = Math.Pow(10, precision);
        var points = new List<(double, double)>(encoded.Length / 4);
        var index = 0;
        var lat = 0;
        var lng = 0;

        while (index < encoded.Length)
        {
            if (!TryReadDelta(encoded, ref index, out var deltaLat)) break;
            if (!TryReadDelta(encoded, ref index, out var deltaLng)) break;
            lat += deltaLat;
            lng += deltaLng;
            points.Add((lat / factor, lng / factor));
        }

        return points;
    }

    /// <summary>
    /// Returns the first GPS point of an encoded polyline, or <c>null</c>
    /// when the input does not decode to at least one valid coordinate.
    /// </summary>
    /// <param name="encoded">Encoded polyline string.</param>
    public static (double Latitude, double Longitude)? FirstPoint(string? encoded)
    {
        var points = Decode(encoded);
        return points.Count == 0 ? null : points[0];
    }

    private static bool TryReadDelta(string encoded, ref int index, out int value)
    {
        value = 0;
        var shift = 0;
        int b;
        do
        {
            if (index >= encoded.Length) return false;
            b = encoded[index++] - 63;
            if (b < 0) return false;
            value |= (b & 0x1F) << shift;
            shift += 5;
            if (shift > 30) return false;
        } while (b >= 0x20);

        value = (value & 1) != 0 ? ~(value >> 1) : (value >> 1);
        return true;
    }
}
