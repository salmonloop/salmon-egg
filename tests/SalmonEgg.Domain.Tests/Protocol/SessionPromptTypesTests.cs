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
    public void SessionPromptParams_MessageId_Should_Serialize_WhenPresent()
    {
        var sessionParams = new SessionPromptParams
        {
            SessionId = "test-session",
            Prompt = new List<ContentBlock>
            {
                new TextContentBlock { Text = "Hello, world!" }
            },
            MessageId = "client-msg-1"
        };

        var json = JsonSerializer.Serialize(sessionParams, CreateJsonOptions());
        var parsed = JsonDocument.Parse(json);

        Assert.That(parsed.RootElement.TryGetProperty("messageId", out var messageId), Is.True);
        Assert.That(messageId.GetString(), Is.EqualTo("client-msg-1"));
    }

    [Test]
    public void SessionPromptResponse_UserMessageId_Should_Deserialize_WhenPresent()
    {
        var json = """
        {
          "stopReason": "end_turn",
          "userMessageId": "server-msg-1"
        }
        """;

        var parsed = JsonSerializer.Deserialize<SessionPromptResponse>(json, CreateJsonOptions());

        Assert.That(parsed, Is.Not.Null);
        Assert.That(parsed!.StopReason, Is.EqualTo(StopReason.EndTurn));
        Assert.That(parsed.UserMessageId, Is.EqualTo("server-msg-1"));
    }
}
