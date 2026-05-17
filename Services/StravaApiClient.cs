using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Web;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyHomePage.Abstractions;
using MyHomePage.Models;
using MyHomePage.Options;

namespace MyHomePage.Services;

/// <summary>
/// HTTP transport for the subset of the Strava REST API the integration
/// consumes. Registered as a typed <see cref="HttpClient"/> so it can be
/// mocked in tests and so connection lifetime is managed by
/// <see cref="IHttpClientFactory"/>.
/// </summary>
public sealed class StravaApiClient : IStravaApiClient
{
    private const string OauthTokenEndpoint = "https://www.strava.com/oauth/token";
    private const string ActivitiesBase = "https://www.strava.com/api/v3";

    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http;
    private readonly StravaOptions _options;
    private readonly ILogger<StravaApiClient> _logger;

    /// <summary>
    /// Initialises the client with a typed <see cref="HttpClient"/> and
    /// the application's Strava configuration.
    /// </summary>
    /// <param name="http">HTTP client supplied by the factory.</param>
    /// <param name="options">Bound Strava options (client id/secret).</param>
    /// <param name="logger">Structured logger for diagnostics.</param>
    public StravaApiClient(
        HttpClient http,
        IOptions<StravaOptions> options,
        ILogger<StravaApiClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<OperationResult<StravaTokenResponse>> ExchangeCodeAsync(
        string authorizationCode,
        CancellationToken cancellationToken = default) =>
        PostTokenRequestAsync(new Dictionary<string, string>
        {
            ["client_id"] = _options.ClientId,
            ["client_secret"] = _options.ClientSecret,
            ["code"] = authorizationCode,
            ["grant_type"] = "authorization_code"
        }, cancellationToken);

    /// <inheritdoc />
    public Task<OperationResult<StravaTokenResponse>> RefreshTokenAsync(
        string refreshToken,
        CancellationToken cancellationToken = default) =>
        PostTokenRequestAsync(new Dictionary<string, string>
        {
            ["client_id"] = _options.ClientId,
            ["client_secret"] = _options.ClientSecret,
            ["refresh_token"] = refreshToken,
            ["grant_type"] = "refresh_token"
        }, cancellationToken);

    /// <inheritdoc />
    public async Task<OperationResult<StravaActivity>> GetActivityAsync(
        string accessToken,
        long activityId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = BuildAuthorizedGet(
                accessToken,
                $"{ActivitiesBase}/activities/{activityId}?include_all_efforts=false");
            using var response = await _http.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
                return await FailFromHttpAsync<StravaActivity>(response, cancellationToken);

            var activity = await response.Content.ReadFromJsonAsync<StravaActivity>(
                JsonOptions, cancellationToken);
            return activity is null
                ? OperationResult<StravaActivity>.Failure("Strava returned an empty activity body.")
                : OperationResult<StravaActivity>.Success(activity);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Strava activity {Id} fetch failed", activityId);
            return OperationResult<StravaActivity>.Failure(ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<StravaGear>> GetGearAsync(
        string accessToken,
        string gearId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(gearId))
            return OperationResult<StravaGear>.Failure("Gear id was not supplied.");

        try
        {
            using var request = BuildAuthorizedGet(
                accessToken,
                $"{ActivitiesBase}/gear/{Uri.EscapeDataString(gearId)}");
            using var response = await _http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return await FailFromHttpAsync<StravaGear>(response, cancellationToken);

            var gear = await response.Content.ReadFromJsonAsync<StravaGear>(
                JsonOptions, cancellationToken);
            return gear is null
                ? OperationResult<StravaGear>.Failure("Strava returned an empty gear body.")
                : OperationResult<StravaGear>.Success(gear);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Strava gear {Id} fetch failed", gearId);
            return OperationResult<StravaGear>.Failure(ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<StravaActivity>>> ListAthleteActivitiesAsync(
        string accessToken,
        int page = 1,
        int perPage = 30,
        CancellationToken cancellationToken = default)
    {
        var perPageCapped = Math.Clamp(perPage, 1, 30);
        var pageNumber = Math.Max(1, page);
        var url = $"{ActivitiesBase}/athlete/activities?page={pageNumber}&per_page={perPageCapped}";

        try
        {
            using var request = BuildAuthorizedGet(accessToken, url);
            using var response = await _http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return await FailFromHttpAsync<IReadOnlyList<StravaActivity>>(response, cancellationToken);

            var list = await response.Content.ReadFromJsonAsync<List<StravaActivity>>(
                JsonOptions, cancellationToken);
            return OperationResult<IReadOnlyList<StravaActivity>>.Success(
                list ?? new List<StravaActivity>());
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Strava recent activities fetch failed");
            return OperationResult<IReadOnlyList<StravaActivity>>.Failure(ex.Message);
        }
    }

    private async Task<OperationResult<StravaTokenResponse>> PostTokenRequestAsync(
        IDictionary<string, string> form,
        CancellationToken cancellationToken)
    {
        try
        {
            using var content = new FormUrlEncodedContent(form);
            using var response = await _http.PostAsync(OauthTokenEndpoint, content, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return await FailFromHttpAsync<StravaTokenResponse>(response, cancellationToken);

            var body = await response.Content.ReadFromJsonAsync<StravaTokenResponse>(
                JsonOptions, cancellationToken);
            return body is null
                ? OperationResult<StravaTokenResponse>.Failure("Strava returned an empty token body.")
                : OperationResult<StravaTokenResponse>.Success(body);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Strava token endpoint call failed");
            return OperationResult<StravaTokenResponse>.Failure(ex.Message);
        }
    }

    private static HttpRequestMessage BuildAuthorizedGet(string accessToken, string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    private async Task<OperationResult<T>> FailFromHttpAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogWarning(
            "Strava API call failed: {Status} {Reason} - body {Body}",
            (int)response.StatusCode, response.ReasonPhrase, Truncate(body, 500));
        return OperationResult<T>.Failure(
            $"Strava API returned {(int)response.StatusCode} {response.ReasonPhrase}.");
    }

    private static string Truncate(string value, int max) =>
        string.IsNullOrEmpty(value) || value.Length <= max ? value : value[..max] + "…";

    /// <summary>
    /// Builds the consent URL the user is redirected to. Static so it can
    /// be used from endpoint registration without resolving the client.
    /// </summary>
    /// <param name="options">Strava options containing client id, redirect uri and scope.</param>
    /// <param name="state">Anti-forgery state parameter echoed in the callback.</param>
    /// <returns>Fully-qualified Strava OAuth authorize URL.</returns>
    public static string BuildAuthorizeUrl(StravaOptions options, string state)
    {
        ArgumentNullException.ThrowIfNull(options);
        var query = HttpUtility.ParseQueryString(string.Empty);
        query["client_id"] = options.ClientId;
        query["redirect_uri"] = options.RedirectUri;
        query["response_type"] = "code";
        query["scope"] = options.Scope;
        query["approval_prompt"] = "auto";
        query["state"] = state;
        return $"https://www.strava.com/oauth/authorize?{query}";
    }
}
