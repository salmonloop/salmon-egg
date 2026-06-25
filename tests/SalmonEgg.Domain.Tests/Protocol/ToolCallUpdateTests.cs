using System.Text.Json;
using NUnit.Framework;
using SalmonEgg.Domain.Models.Protocol;
using SalmonEgg.Domain.Models.Tool;
using SalmonEgg.Infrastructure.Serialization;

namespace SalmonEgg.Domain.Tests.Protocol;

[TestFixture]
public sealed class ToolCallUpdateTests
{
    private readonly JsonSerializerOptions _jsonOptions;

    public ToolCallUpdateTests()
    {
        // Configure serialization options to match the codebase
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };

        // Add the JsonPropertyNameEnumConverterFactory to honor JsonPropertyName attributes on enums
        _jsonOptions.Converters.Add(new JsonPropertyNameEnumConverterFactory());
    }

    [Test]
    public void ToolCallUpdate_DeserializesKnownFields_AndIgnoresUnknownLegacyField()
    {
        var json = """
        {
          "toolCallId": "call-1",
          "title": "Switch mode",
          "kind": "switch_mode",
          "status": "completed",
          "toolCall": { "legacy": true }
        }
        """;

        var update = JsonSerializer.Deserialize<ToolCallUpdate>(json, _jsonOptions);

        Assert.That(update, Is.Not.Null);
        Assert.That(update!.ToolCallId, Is.EqualTo("call-1"));
        Assert.That(update.Title, Is.EqualTo("Switch mode"));
        Assert.That(update.Kind, Is.EqualTo(ToolCallKind.SwitchMode));
        Assert.That(update.Status, Is.EqualTo(ToolCallStatus.Completed));
    }

    [Test]
    public void ToolCallUpdate_Should_Serialize_Correctly()
    {
        // Given: A ToolCallUpdate with required fields
        var update = new ToolCallUpdate
        {
            ToolCallId = "test-call-123",
            Title = "Test Tool Call",
            Kind = ToolCallKind.Execute,
            Status = ToolCallStatus.Pending
        };

        // When: Serialize to JSON
        var json = JsonSerializer.Serialize(update, _jsonOptions);
        var parsed = JsonDocument.Parse(json);

        // Then: Required fields should be present
        Assert.That(parsed.RootElement.TryGetProperty("toolCallId", out var toolCallId), Is.True);
        Assert.That(toolCallId.GetString(), Is.EqualTo("test-call-123"));
        Assert.That(parsed.RootElement.TryGetProperty("title", out var title), Is.True);
        Assert.That(title.GetString(), Is.EqualTo("Test Tool Call"));
        Assert.That(parsed.RootElement.TryGetProperty("kind", out var kind), Is.True);
        Assert.That(kind.GetString(), Is.EqualTo("execute"));
        Assert.That(parsed.RootElement.TryGetProperty("status", out var status), Is.True);
        Assert.That(status.GetString(), Is.EqualTo("pending"));
    }

    [Test]
    public void ToolCallUpdate_SwitchModeKind_Should_Serialize_ToSchemaValue()
    {
        var update = new ToolCallUpdate
        {
            ToolCallId = "switch-1",
            Title = "Switch to plan",
            Kind = ToolCallKind.SwitchMode
        };

        var json = JsonSerializer.Serialize(update, _jsonOptions);
        var parsed = JsonDocument.Parse(json);

        Assert.That(parsed.RootElement.GetProperty("kind").GetString(), Is.EqualTo("switch_mode"));
    }
}
