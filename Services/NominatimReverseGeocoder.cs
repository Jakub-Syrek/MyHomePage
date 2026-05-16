using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using MyHomePage.Abstractions;

namespace MyHomePage.Services;

/// <summary>
/// Reverse geocodes GPS coordinates via the OpenStreetMap Nominatim
/// service — free and unauthenticated. Returns the most specific
/// available label out of (suburb / town / city / state), suffixed with
/// the country code so a city named "Cambridge" stays unambiguous.
///
/// Honours Nominatim's usage policy by sending a descriptive User-Agent.
/// Failures (network, rate-limit, parse) are swallowed and converted to
/// <c>null</c> so callers can chain further fallbacks.
/// </summary>
public sealed class NominatimReverseGeocoder : IReverseGeocoder
{
    private const string BaseUrl = "https://nominatim.openstreetmap.org/reverse";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly HttpClient _http;
    private readonly ILogger<NominatimReverseGeocoder> _logger;

    /// <summary>
    /// Creates a new reverse geocoder. The injected <see cref="HttpClient"/>
    /// is configured with the required User-Agent header inside the
    /// constructor — Nominatim rejects requests without one.
    /// </summary>
    /// <param name="http">HTTP client supplied by the factory.</param>
    /// <param name="logger">Structured logger for diagnostics.</param>
    public NominatimReverseGeocoder(
        HttpClient http,
        ILogger<NominatimReverseGeocoder> logger)
    {
        _http = http;
        _logger = logger;
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
        {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(
                "MyMountainAdventures/1.0 (https://mountains.cruxbeta.net)");
        }
        _http.Timeout = TimeSpan.FromSeconds(8);
    }

    /// <inheritdoc />
    public async Task<string?> ResolveAsync(
        double latitude,
        double longitude,
        CancellationToken cancellationToken = default)
    {
        var url =
            $"{BaseUrl}?lat={latitude.ToString(CultureInfo.InvariantCulture)}" +
            $"&lon={longitude.ToString(CultureInfo.InvariantCulture)}" +
            "&format=json&zoom=14&addressdetails=1&accept-language=en";

        try
        {
            using var response = await _http.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug(
                    "Nominatim returned {Status} for ({Lat}, {Lng})",
                    (int)response.StatusCode, latitude, longitude);
                return null;
            }

            var payload = await response.Content.ReadFromJsonAsync<NominatimResponse>(
                JsonOptions, cancellationToken);
            return ComposeLabel(payload?.Address);
        }
        catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex,
                "Nominatim reverse-geocode failed for ({Lat}, {Lng})",
                latitude, longitude);
            return null;
        }
    }

    private static string? ComposeLabel(NominatimAddress? address)
    {
        if (address is null) return null;

        // Pick the most specific populated tier so we get "Krakow" not
        // "Lesser Poland Voivodeship" when both are present.
        var primary =
            address.Suburb
            ?? address.Neighbourhood
            ?? address.Village
            ?? address.Town
            ?? address.City
            ?? address.County
            ?? address.State;

        if (string.IsNullOrWhiteSpace(primary)) return null;

        return string.IsNullOrWhiteSpace(address.Country)
            ? primary
            : $"{primary}, {address.Country}";
    }

    private sealed class NominatimResponse
    {
        [JsonPropertyName("address")]
        public NominatimAddress? Address { get; set; }
    }

    private sealed class NominatimAddress
    {
        [JsonPropertyName("suburb")] public string? Suburb { get; set; }
        [JsonPropertyName("neighbourhood")] public string? Neighbourhood { get; set; }
        [JsonPropertyName("village")] public string? Village { get; set; }
        [JsonPropertyName("town")] public string? Town { get; set; }
        [JsonPropertyName("city")] public string? City { get; set; }
        [JsonPropertyName("county")] public string? County { get; set; }
        [JsonPropertyName("state")] public string? State { get; set; }
        [JsonPropertyName("country")] public string? Country { get; set; }
    }
}
