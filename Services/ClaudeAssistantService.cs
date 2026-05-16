using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MyHomePage.Abstractions;
using MyHomePage.Models;

namespace MyHomePage.Services;

/// <summary>
/// Calls Anthropic's Messages API to generate suggested title / description /
/// location for a media upload, given a gallery category and a few keywords.
///
/// Configuration:
///   * Env var <c>ANTHROPIC_API_KEY</c> — required, secret.
///   * Env var <c>ANTHROPIC_MODEL</c>  — optional, default "claude-haiku-4-7".
///
/// Implementation notes:
///   * Uses a forced tool call to guarantee structured JSON output.
///   * System prompt is cached (1h TTL) — saves ~90% on subsequent calls
///     made within the cache window.
///   * Strict timeout of 30s per request; HttpClient is injected via
///     IHttpClientFactory so it can be pooled.
/// </summary>
public sealed class ClaudeAssistantService : IAiAssistantService
{
    private const string ApiUrl = "https://api.anthropic.com/v1/messages";
    private const string AnthropicVersion = "2023-06-01";
    private const string DefaultModel = "claude-haiku-4-7";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // Stable system prompt — cached by the API.
    private const string SystemPrompt = """
        You are an outdoor-adventure copywriter for a personal mountain-sports gallery
        called "My Mountain Adventures". A user uploads a collection of photos and/or
        videos under one of these categories:

          - Mountains          (hiking, trekking, alpine scrambles)
          - Rock Climbing      (outdoor sport / trad / multi-pitch routes)
          - Bouldering         (outdoor or indoor, short powerful problems)
          - Indoor Climbing    (rope routes in a climbing gym)

        Given the category and a few keywords, produce concise, evocative metadata.

        STYLE:
          - Title: 3-6 words, evocative, no clickbait. Title-case English.
          - Description: 1-3 sentences (max ~200 chars). Sensory, vivid, factual.
          - Location: a real place that fits the keywords if obvious (e.g. "Tatra
            Mountains, Poland") or null when unclear. Never invent precise venues.
          - Latitude / Longitude: only if the user named a specific landmark you
            actually recognise; otherwise leave null. Never guess.

        Reply ONLY by calling the suggest_upload_metadata tool.
        """;

    private readonly HttpClient _http;
    private readonly ILogger<ClaudeAssistantService> _logger;
    private readonly string? _apiKey;
    private readonly string _model;

    public ClaudeAssistantService(HttpClient http, ILogger<ClaudeAssistantService> logger)
    {
        _http = http;
        _logger = logger;
        _apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        _model = Environment.GetEnvironmentVariable("ANTHROPIC_MODEL") ?? DefaultModel;
        _http.Timeout = TimeSpan.FromSeconds(30);
    }

    public bool IsEnabled => !string.IsNullOrWhiteSpace(_apiKey);

    public async Task<UploadSuggestion?> SuggestForUploadAsync(
        string category,
        string keywords,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            _logger.LogWarning("AI assistant called but ANTHROPIC_API_KEY is not set");
            return null;
        }

        category = (category ?? "").Trim();
        keywords = (keywords ?? "").Trim();
        if (string.IsNullOrWhiteSpace(category) && string.IsNullOrWhiteSpace(keywords))
            return null;

        var userMessage = string.IsNullOrWhiteSpace(category)
            ? $"Keywords: {keywords}"
            : $"Category: {category}\nKeywords: {keywords}";

        var payload = new
        {
            model = _model,
            max_tokens = 600,
            // Cached system block — costs full price once, then ~10% per hit within TTL
            system = new object[]
            {
                new
                {
                    type = "text",
                    text = SystemPrompt,
                    cache_control = new { type = "ephemeral" }
                }
            },
            tools = new object[]
            {
                new
                {
                    name = "suggest_upload_metadata",
                    description = "Return suggested title, description and (when known) location for a gallery upload.",
                    input_schema = new
                    {
                        type = "object",
                        properties = new
                        {
                            title = new { type = "string", description = "3-6 words, evocative." },
                            description = new { type = "string", description = "1-3 sentences, max ~200 chars." },
                            location = new { type = new[] { "string", "null" }, description = "Real place name or null when unsure." },
                            latitude = new { type = new[] { "number", "null" }, description = "Only if certain about a named landmark." },
                            longitude = new { type = new[] { "number", "null" }, description = "Only if certain about a named landmark." }
                        },
                        required = new[] { "title", "description" }
                    }
                }
            },
            tool_choice = new { type = "tool", name = "suggest_upload_metadata" },
            messages = new object[]
            {
                new { role = "user", content = userMessage }
            }
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, ApiUrl);
        req.Headers.Add("x-api-key", _apiKey);
        req.Headers.Add("anthropic-version", AnthropicVersion);
        req.Content = JsonContent.Create(payload, options: JsonOpts);

        HttpResponseMessage resp;
        try
        {
            resp = await _http.SendAsync(req, cancellationToken);
        }
        catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Anthropic request failed (network)");
            return null;
        }

        if (!resp.IsSuccessStatusCode)
        {
            var errorBody = await resp.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("Anthropic API returned {Status}: {Body}", resp.StatusCode, errorBody);
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(cancellationToken));
            if (!doc.RootElement.TryGetProperty("content", out var content)) return null;

            foreach (var block in content.EnumerateArray())
            {
                if (block.TryGetProperty("type", out var t)
                    && t.GetString() == "tool_use"
                    && block.TryGetProperty("input", out var input))
                {
                    return new UploadSuggestion
                    {
                        Title = input.GetPropOrEmpty("title"),
                        Description = input.GetPropOrEmpty("description"),
                        Location = input.GetNullableString("location"),
                        Latitude = input.GetNullableDouble("latitude"),
                        Longitude = input.GetNullableDouble("longitude")
                    };
                }
            }
            _logger.LogWarning("Anthropic response did not contain a tool_use block");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse Anthropic response");
            return null;
        }
    }
}

internal static class JsonElementExtensions
{
    public static string GetPropOrEmpty(this JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
           ? v.GetString() ?? "" : "";

    public static string? GetNullableString(this JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v)) return null;
        return v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }

    public static double? GetNullableDouble(this JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v)) return null;
        return v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) ? d : null;
    }
}
