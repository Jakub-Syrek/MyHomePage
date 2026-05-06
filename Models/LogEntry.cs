using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace MyHomePage.Models;

/// <summary>
/// Represents a single log entry in Serilog's CLEF (Compact Log Event Format).
/// Each line of the .clef file deserialises into one of these.
/// </summary>
public sealed class LogEntry
{
    [JsonPropertyName("@t")] public DateTimeOffset Timestamp { get; set; }
    [JsonPropertyName("@mt")] public string MessageTemplate { get; set; } = "";
    [JsonPropertyName("@l")] public string? Level { get; set; }   // null → Information
    [JsonPropertyName("@x")] public string? Exception { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Properties { get; set; }

    /// <summary>Returns the log level, defaulting to "Information" when absent.</summary>
    public string EffectiveLevel => Level ?? "Information";

    /// <summary>
    /// Renders the structured message template by substituting {Property} placeholders
    /// with their actual values from the Properties dictionary.
    /// </summary>
    public string RenderedMessage
    {
        get
        {
            if (Properties == null || Properties.Count == 0)
                return MessageTemplate;

            return Regex.Replace(MessageTemplate, @"\{(\w+)(?::[^}]*)?\}", match =>
            {
                var key = match.Groups[1].Value;
                if (Properties.TryGetValue(key, out var el))
                {
                    return el.ValueKind == JsonValueKind.String
                        ? el.GetString() ?? match.Value
                        : el.ToString();
                }
                return match.Value;
            });
        }
    }

    /// <summary>Short source context — strips namespace, keeps only the class name.</summary>
    public string ShortSource
    {
        get
        {
            if (Properties != null &&
                Properties.TryGetValue("SourceContext", out var sc) &&
                sc.ValueKind == JsonValueKind.String)
            {
                var full = sc.GetString() ?? "";
                var dot = full.LastIndexOf('.');
                return dot >= 0 ? full[(dot + 1)..] : full;
            }
            return "";
        }
    }
}
