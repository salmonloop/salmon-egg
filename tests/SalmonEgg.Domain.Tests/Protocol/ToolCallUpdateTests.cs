using System.Text.Json;
using Xunit;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Acp.Tool;

namespace SalmonEgg.Domain.Tests.Protocol;

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
    }

    [Fact]
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

        Assert.NotNull(update);
        Assert.Equal("call-1", update!.ToolCallId);
        Assert.Equal("Switch mode", update.Title);
        Assert.Equal(ToolCallKind.SwitchMode, update.Kind);
        Assert.Equal(ToolCallStatus.Completed, update.Status);
    }

    [Fact]
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
        Assert.True(parsed.RootElement.TryGetProperty("toolCallId", out var toolCallId));
        Assert.Equal("test-call-123", toolCallId.GetString());
        Assert.True(parsed.RootElement.TryGetProperty("title", out var title));
        Assert.Equal("Test Tool Call", title.GetString());
        Assert.True(parsed.RootElement.TryGetProperty("kind", out var kind));
        Assert.Equal("execute", kind.GetString());
        Assert.True(parsed.RootElement.TryGetProperty("status", out var status));
        Assert.Equal("pending", status.GetString());
    }

    [Fact]
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

        Assert.Equal("switch_mode", parsed.RootElement.GetProperty("kind").GetString());
    }
}
