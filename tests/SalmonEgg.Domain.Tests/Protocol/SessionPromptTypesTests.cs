using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using NUnit.Framework;
using SalmonEgg.Domain.Models.Content;
using SalmonEgg.Domain.Models.Protocol;
using SalmonEgg.Domain.Models.Session;

namespace SalmonEgg.Domain.Tests.Protocol;

[TestFixture]
public sealed class SessionPromptTypesTests
{
    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        return options;
    }

    [Test]
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

        Assert.That(parsed, Is.Not.Null);
        Assert.That(parsed!.Prompt, Is.Not.Null);
        Assert.That(parsed.Prompt, Has.Count.EqualTo(1));
        Assert.That(parsed.Prompt![0], Is.TypeOf<TextContentBlock>());
    }

    [Test]
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
        Assert.That(parsed.RootElement.TryGetProperty("prompt", out var prompt), Is.True);
        Assert.That(prompt.ValueKind, Is.EqualTo(JsonValueKind.Array));
    }

    [Test]
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

        Assert.That(parsed.RootElement.TryGetProperty("sessionId", out _), Is.True);
        Assert.That(parsed.RootElement.TryGetProperty("prompt", out _), Is.True);
        Assert.That(parsed.RootElement.TryGetProperty("maxTokens", out _), Is.False);
        Assert.That(parsed.RootElement.TryGetProperty("stopSequences", out _), Is.False);
        Assert.That(parsed.RootElement.TryGetProperty("messageId", out _), Is.False);
    }

    [Test]
    public void SessionPromptResponse_Should_Serialize_OnlyOfficialRootFields()
    {
        var response = new SessionPromptResponse(StopReason.EndTurn);
        var json = JsonSerializer.Serialize(response, CreateJsonOptions());
        using var parsed = JsonDocument.Parse(json);

        Assert.That(parsed.RootElement.TryGetProperty("stopReason", out var stopReason), Is.True);
        Assert.That(stopReason.GetString(), Is.EqualTo("end_turn"));
        Assert.That(parsed.RootElement.TryGetProperty("userMessageId", out _), Is.False);
    }
}
