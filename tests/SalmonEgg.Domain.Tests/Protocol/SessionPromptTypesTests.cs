using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;
using SalmonEgg.Acp.Content;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Domain.Models.Session;

namespace SalmonEgg.Domain.Tests.Protocol;

public sealed class SessionPromptTypesTests
{
    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        return options;
    }

    [Fact]
    public void SessionPromptParams_Prompt_Should_Deserialize_As_ContentBlock_List()
    {
        var json = """
        {
          "sessionId": "test-session",
          "prompt": [
            { "type": "text", "text": "Hello, world!" }
          ]
        }
        """;

        var parsed = JsonSerializer.Deserialize<SessionPromptParams>(json);

        Assert.NotNull(parsed);
        Assert.NotNull(parsed!.Prompt);
        var prompt = Assert.Single(parsed.Prompt);
        Assert.IsType<TextContentBlock>(prompt);
    }

    [Fact]
    public void SessionPromptParams_Prompt_Should_Serialize_As_Array()
    {
        // Given: A SessionPromptParams with content blocks
        var sessionParams = new SessionPromptParams
        {
            SessionId = "test-session",
            Prompt = new List<ContentBlock>
            {
                new TextContentBlock { Text = "Hello, world!" }
            }
        };

        // When: Serialize to JSON
        var json = JsonSerializer.Serialize(sessionParams);
        var parsed = JsonDocument.Parse(json);

        // Then: prompt should be an array in JSON
        Assert.True(parsed.RootElement.TryGetProperty("prompt", out var prompt));
        Assert.Equal(JsonValueKind.Array, prompt.ValueKind);
    }

    [Fact]
    public void SessionPromptParams_Should_Serialize_OnlyOfficialRootFields()
    {
        var sessionParams = new SessionPromptParams
        {
            SessionId = "test-session",
            Prompt = new List<ContentBlock>
            {
                new TextContentBlock { Text = "Hello, world!" }
            }
        };

        var json = JsonSerializer.Serialize(sessionParams, CreateJsonOptions());
        var parsed = JsonDocument.Parse(json);

        Assert.True(parsed.RootElement.TryGetProperty("sessionId", out _));
        Assert.True(parsed.RootElement.TryGetProperty("prompt", out _));
        Assert.False(parsed.RootElement.TryGetProperty("maxTokens", out _));
        Assert.False(parsed.RootElement.TryGetProperty("stopSequences", out _));
        Assert.False(parsed.RootElement.TryGetProperty("messageId", out _));
    }

    [Fact]
    public void SessionPromptResponse_Should_Serialize_OnlyOfficialRootFields()
    {
        var response = new SessionPromptResponse(StopReason.EndTurn);
        var json = JsonSerializer.Serialize(response, CreateJsonOptions());
        using var parsed = JsonDocument.Parse(json);

        Assert.True(parsed.RootElement.TryGetProperty("stopReason", out var stopReason));
        Assert.Equal("end_turn", stopReason.GetString());
        Assert.False(parsed.RootElement.TryGetProperty("userMessageId", out _));
    }
}
