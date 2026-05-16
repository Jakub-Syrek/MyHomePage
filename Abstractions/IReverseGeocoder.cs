namespace MyHomePage.Abstractions;

/// <summary>
/// Resolves a free-text location label (city / town / region) from a
/// pair of GPS coordinates. Used as a fallback when an activity's
/// structured location fields are empty but it does have a GPS fix.
///
/// Implementations are expected to be I/O-bound (typically a single
/// HTTP call to a public geocoding service) and tolerant of failure —
/// returning <c>null</c> rather than throwing — so callers can chain
/// further fallbacks without try/catch boilerplate.
/// </summary>
public interface IReverseGeocoder
{
    /// <summary>
    /// Attempts to resolve a human-readable location label for the given
    /// coordinates.
    /// </summary>
    /// <param name="latitude">Decimal-degree latitude (WGS84).</param>
    /// <param name="longitude">Decimal-degree longitude (WGS84).</param>
    /// <param name="cancellationToken">Token used to cancel the HTTP call.</param>
    /// <returns>The resolved label or <c>null</c> on failure / no match.</returns>
    Task<string?> ResolveAsync(
        double latitude,
        double longitude,
        CancellationToken cancellationToken = default);
}
