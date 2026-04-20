using System.Collections.Generic;
using System.Text.Json;
using SalmonEgg.Domain.Models.JsonRpc;
using SalmonEgg.Domain.Models.Content;
using SalmonEgg.Domain.Models.Protocol;
using SalmonEgg.Domain.Models.Plan;
using SalmonEgg.Domain.Models.Session;
using SalmonEgg.Infrastructure.Serialization;

namespace SalmonEgg.Infrastructure.Tests.Serialization;

public class MessageParserTests
{
    [Fact]
    public void Options_ShouldDeserialize_PlanUpdate_WithSnakeCaseEnumValues()
    {
        var json = """
        {
          "sessionId": "sess_test",
          "update": {
            "sessionUpdate": "plan",
            "entries": [
              { "content": "Work Items", "status": "in_progress", "priority": "medium" }
            ]
          }
        }
        """;

        var parser = new MessageParser();
        var updateParams = JsonSerializer.Deserialize<SessionUpdateParams>(json, parser.Options);

        Assert.NotNull(updateParams);
        Assert.NotNull(updateParams!.Update);
        Assert.IsType<PlanUpdate>(updateParams.Update);

        var plan = (PlanUpdate)updateParams.Update;
        Assert.NotNull(plan.Entries);
        Assert.Single(plan.Entries!);
        Assert.Equal(PlanEntryStatus.InProgress, plan.Entries![0].Status);
        Assert.Equal(PlanEntryPriority.Medium, plan.Entries![0].Priority);
    }

    [Fact]
    public void SerializeMessage_ShouldOmitNullOptionalFields()
    {
        var parser = new MessageParser();

        var promptParams = new SessionPromptParams
        {
            SessionId = "sess_test",
            Prompt = new List<ContentBlock> { new TextContentBlock { Text = "hi" } },
            MaxTokens = null,
            StopSequences = null
        };

        var request = new JsonRpcRequest(
            id: 3,
            method: "session/prompt",
            @params: JsonSerializer.SerializeToElement(promptParams, typeof(SessionPromptParams), parser.Options));

        var json = parser.SerializeMessage(request);

        Assert.DoesNotContain("\"maxTokens\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"stopSequences\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void SerializeMessage_ShouldKeepResultNull_ForJsonRpcResponses()
    {
        var parser = new MessageParser();

        // JSON-RPC responses must include either "result" or "error". For some ACP methods the result is null.
        var nullResult = JsonSerializer.SerializeToElement<object?>(null, parser.Options);
        var response = new JsonRpcResponse(id: 1, result: nullResult);

        var json = parser.SerializeMessage(response);

        Assert.Contains("\"result\":null", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"error\"", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("end_turn", StopReason.EndTurn)]
    [InlineData("max_tokens", StopReason.MaxTokens)]
    [InlineData("max_turn_requests", StopReason.MaxTurnRequests)]
    [InlineData("refusal", StopReason.Refusal)]
    [InlineData("cancelled", StopReason.Cancelled)]
    public void Options_ShouldDeserialize_SessionPromptResponse_WithOfficialStopReasons(string stopReason, StopReason expected)
    {
        var parser = new MessageParser();
        var json = $$"""
        {
          "stopReason": "{{stopReason}}"
        }
        """;

        var response = JsonSerializer.Deserialize<SessionPromptResponse>(json, parser.Options);

        Assert.NotNull(response);
        Assert.Equal(expected, response!.StopReason);
    }

    [Fact]
    public void Options_ShouldDeserialize_UsageUpdate_OfficialMinimalPayload()
    {
        var json = """
        {
          "sessionId": "sess_usage",
          "update": {
            "sessionUpdate": "usage_update",
            "used": 717,
            "size": 200000
          }
        }
        """;

        var parser = new MessageParser();
        var updateParams = JsonSerializer.Deserialize<SessionUpdateParams>(json, parser.Options);

        Assert.NotNull(updateParams);
        var usage = Assert.IsType<UsageUpdate>(updateParams!.Update);
        Assert.Equal(717, usage.Used);
        Assert.Equal(200000, usage.Size);
        Assert.Null(usage.Cost);
    }

    [Fact]
    public void Options_ShouldDeserialize_UsageUpdate_WithOfficialCostObject()
    {
        var json = """
        {
          "sessionId": "sess_usage",
          "update": {
            "sessionUpdate": "usage_update",
            "used": 717,
            "size": 200000,
            "cost": {
              "amount": 0.16861,
              "currency": "USD"
            }
          }
        }
        """;

        var parser = new MessageParser();
        var updateParams = JsonSerializer.Deserialize<SessionUpdateParams>(json, parser.Options);

        Assert.NotNull(updateParams);
        var usage = Assert.IsType<UsageUpdate>(updateParams!.Update);
        Assert.Equal(717, usage.Used);
        Assert.Equal(200000, usage.Size);
        Assert.NotNull(usage.Cost);
        Assert.Equal(0.16861m, usage.Cost!.Amount);
        Assert.Equal("USD", usage.Cost.Currency);
    }

    [Fact]
    public void Options_ShouldDeserialize_SessionPrompt_WithImageUriAndAnnotations()
    {
        var json = """
        {
          "sessionId": "sess_content",
          "prompt": [
            {
              "type": "image",
              "data": "iVBORw0KGgoAAAANSUhEUgAAAAEAAAAB",
              "mimeType": "image/png",
              "uri": "file:///tmp/example.png",
              "annotations": {
                "audience": ["assistant"],
                "priority": 0.75,
                "lastModified": "2026-04-20T00:00:00Z"
              }
            }
          ]
        }
        """;

        var parser = new MessageParser();
        var promptParams = JsonSerializer.Deserialize<SessionPromptParams>(json, parser.Options);

        Assert.NotNull(promptParams);
        Assert.Single(promptParams!.Prompt);

        var image = Assert.IsType<ImageContentBlock>(promptParams.Prompt[0]);
        var roundTripped = JsonSerializer.Serialize(promptParams, parser.Options);
        using var doc = JsonDocument.Parse(roundTripped);
        var promptImage = doc.RootElement.GetProperty("prompt")[0];

        Assert.Equal("image", promptImage.GetProperty("type").GetString());
        Assert.Equal("file:///tmp/example.png", promptImage.GetProperty("uri").GetString());
        Assert.True(promptImage.TryGetProperty("annotations", out var annotations));
        Assert.Equal(0.75m, annotations.GetProperty("priority").GetDecimal());
        Assert.Equal("assistant", annotations.GetProperty("audience")[0].GetString());
        Assert.Equal("file:///tmp/example.png", image.Uri);
    }

    [Fact]
    public void Options_ShouldDeserialize_SessionUpdate_WhenMetaPrecedesDiscriminator()
    {
        var json = """
        {
          "sessionId": "sess_meta",
          "update": {
            "_meta": {
              "claudeCode": {
                "toolName": "Bash"
              }
            },
            "toolCallId": "call-meta-1",
            "sessionUpdate": "tool_call_update",
            "status": "completed",
            "title": "Run command"
          }
        }
        """;

        var parser = new MessageParser();

        var updateParams = JsonSerializer.Deserialize<SessionUpdateParams>(json, parser.Options);

        Assert.NotNull(updateParams);
        var update = Assert.IsType<ToolCallStatusUpdate>(updateParams!.Update);
        Assert.Equal("call-meta-1", update.ToolCallId);
        Assert.Equal("Run command", update.Title);
        Assert.Equal(Domain.Models.Tool.ToolCallStatus.Completed, update.Status);
    }

    [Fact]
    public void Options_ShouldDeserialize_SessionListResponse_WithSessionMeta()
    {
        var json = """
        {
          "sessions": [
            {
              "sessionId": "sess_list_1",
              "cwd": "/home/user/project",
              "title": "Existing session",
              "description": "Session summary",
              "updatedAt": "2026-03-22T19:00:00Z",
              "_meta": {
                "source": "unit-test",
                "rank": 3
              }
            }
          ]
        }
        """;

        var parser = new MessageParser();
        var response = JsonSerializer.Deserialize<SessionListResponse>(json, parser.Options);

        Assert.NotNull(response);
        Assert.Single(response!.Sessions);

        var session = response.Sessions[0];
        Assert.Equal("Session summary", session.Description);

        var meta = session.Meta;
        Assert.NotNull(meta);
        Assert.Equal("unit-test", ReadMetaValue(meta!["source"]));
        Assert.Equal("3", ReadMetaValue(meta["rank"]));
    }

    private static string? ReadMetaValue(object? value)
    {
        return value switch
        {
            null => null,
            JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString(),
            JsonElement element when element.ValueKind == JsonValueKind.Number => element.GetRawText(),
            JsonElement element when element.ValueKind == JsonValueKind.True => bool.TrueString.ToLowerInvariant(),
            JsonElement element when element.ValueKind == JsonValueKind.False => bool.FalseString.ToLowerInvariant(),
            _ => value.ToString()
        };
    }
}

