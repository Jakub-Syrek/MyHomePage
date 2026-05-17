using System.Text.Json;

namespace MyHomePage.Tests.Models;

/// <summary>
/// Tests for <see cref="LogEntry"/> — Serilog CLEF deserialisation,
/// EffectiveLevel default, template rendering with property substitution
/// and SourceContext shortening.
/// </summary>
[TestFixture]
public sealed class LogEntryTests
{
    [Test]
    public void Deserialise_FullClefLine_PopulatesAllProperties()
    {
        const string clef = """
        {
            "@t": "2026-05-17T08:30:00Z",
            "@mt": "User {Email} signed in",
            "@l": "Information",
            "Email": "jaqb@example.com",
            "SourceContext": "MyHomePage.Pages.Login"
        }
        """;

        var entry = JsonSerializer.Deserialize<LogEntry>(
            clef, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.That(entry, Is.Not.Null);
        Assert.That(entry!.MessageTemplate, Is.EqualTo("User {Email} signed in"));
        Assert.That(entry.Level, Is.EqualTo("Information"));
        Assert.That(entry.Properties, Is.Not.Null);
        Assert.That(entry.Properties!["Email"].GetString(), Is.EqualTo("jaqb@example.com"));
    }

    [Test]
    public void EffectiveLevel_NullLevel_DefaultsToInformation()
    {
        var entry = new LogEntry { Level = null };

        Assert.That(entry.EffectiveLevel, Is.EqualTo("Information"));
    }

    [Test]
    public void EffectiveLevel_ExplicitLevel_ReturnsIt()
    {
        var entry = new LogEntry { Level = "Error" };

        Assert.That(entry.EffectiveLevel, Is.EqualTo("Error"));
    }

    [Test]
    public void RenderedMessage_NoProperties_ReturnsTemplateUnchanged()
    {
        var entry = new LogEntry { MessageTemplate = "Plain message", Properties = null };

        Assert.That(entry.RenderedMessage, Is.EqualTo("Plain message"));
    }

    [Test]
    public void RenderedMessage_EmptyProperties_ReturnsTemplateUnchanged()
    {
        var entry = new LogEntry
        {
            MessageTemplate = "Plain message",
            Properties = new Dictionary<string, JsonElement>()
        };

        Assert.That(entry.RenderedMessage, Is.EqualTo("Plain message"));
    }

    [Test]
    public void RenderedMessage_SubstitutesStringProperty()
    {
        var properties = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            """{ "Email": "abc@x.com" }""")!;
        var entry = new LogEntry
        {
            MessageTemplate = "Hello {Email}",
            Properties = properties
        };

        Assert.That(entry.RenderedMessage, Is.EqualTo("Hello abc@x.com"));
    }

    [Test]
    public void RenderedMessage_SubstitutesNumericProperty()
    {
        var properties = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            """{ "Count": 42 }""")!;
        var entry = new LogEntry
        {
            MessageTemplate = "{Count} items",
            Properties = properties
        };

        Assert.That(entry.RenderedMessage, Is.EqualTo("42 items"));
    }

    [Test]
    public void RenderedMessage_MissingPropertyKeepsPlaceholder()
    {
        var properties = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            """{ "Other": "x" }""")!;
        var entry = new LogEntry
        {
            MessageTemplate = "Hello {Email}",
            Properties = properties
        };

        Assert.That(entry.RenderedMessage, Is.EqualTo("Hello {Email}"));
    }

    [Test]
    public void RenderedMessage_PlaceholderWithFormatSpec_StillResolves()
    {
        var properties = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            """{ "Count": 7 }""")!;
        var entry = new LogEntry
        {
            MessageTemplate = "{Count:D2} items",
            Properties = properties
        };

        Assert.That(entry.RenderedMessage, Is.EqualTo("7 items"));
    }

    [Test]
    public void ShortSource_FullyQualified_ReturnsClassOnly()
    {
        var properties = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            """{ "SourceContext": "MyHomePage.Services.StravaSyncService" }""")!;
        var entry = new LogEntry { Properties = properties };

        Assert.That(entry.ShortSource, Is.EqualTo("StravaSyncService"));
    }

    [Test]
    public void ShortSource_NoDots_ReturnsAsIs()
    {
        var properties = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            """{ "SourceContext": "Program" }""")!;
        var entry = new LogEntry { Properties = properties };

        Assert.That(entry.ShortSource, Is.EqualTo("Program"));
    }

    [Test]
    public void ShortSource_NoSourceContext_ReturnsEmpty()
    {
        var entry = new LogEntry { Properties = null };

        Assert.That(entry.ShortSource, Is.EqualTo(""));
    }

    [Test]
    public void ShortSource_NumericSourceContext_ReturnsEmpty()
    {
        // The accessor only handles string-valued SourceContext.
        var properties = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            """{ "SourceContext": 123 }""")!;
        var entry = new LogEntry { Properties = properties };

        Assert.That(entry.ShortSource, Is.EqualTo(""));
    }
}
