using System.Collections.Generic;
using System.Text.Json;
using SalmonEgg.Domain.Models.JsonRpc;
using SalmonEgg.Domain.Models.Content;
using SalmonEgg.Domain.Models.Protocol;
using SalmonEgg.Domain.Models.Plan;
using SalmonEgg.Domain.Models.Session;
using SalmonEgg.Domain.Models.Tool;
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
    public void Options_ShouldSerialize_SessionPromptParams_WithMessageId()
    {
        var parser = new MessageParser();
        var parameters = new SessionPromptParams(
            "sess-1",
            new List<ContentBlock> { new TextContentBlock("hello") },
            messageId: "client-msg-42");

        var json = JsonSerializer.Serialize(parameters, parser.Options);
        using var doc = JsonDocument.Parse(json);

        Assert.Equal("client-msg-42", doc.RootElement.GetProperty("messageId").GetString());
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
    public void Options_ShouldDeserialize_SessionPromptResponse_WithUserMessageId()
    {
        var parser = new MessageParser();
        var json = """
        {
          "stopReason": "end_turn",
          "userMessageId": "server-msg-42"
        }
        """;

        var response = JsonSerializer.Deserialize<SessionPromptResponse>(json, parser.Options);

        Assert.NotNull(response);
        Assert.Equal(StopReason.EndTurn, response!.StopReason);
        Assert.Equal("server-msg-42", response.UserMessageId);
    }

    [Fact]
    public void Options_ShouldDeserialize_UsageUpdate_OfficialMinimalPayload()
    {
        var json = """
        {
          "sessionId": "sess_usage",
          "update": {
            "sessionUpdate": "usage_update",
            "used": 53000,
            "size": 200000
          }
        }
        """;

        var parser = new MessageParser();
        var updateParams = JsonSerializer.Deserialize<SessionUpdateParams>(json, parser.Options);

        Assert.NotNull(updateParams);
        var usage = Assert.IsType<UsageUpdate>(updateParams!.Update);
        Assert.Equal(53000, usage.Used);
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
    public void Options_ShouldDeserialize_ToolCallStatusUpdate_OfficialPartialPayload()
    {
        var json = """
        {
          "sessionId": "sess_abc123def456",
          "update": {
            "sessionUpdate": "tool_call_update",
            "toolCallId": "call_001",
            "status": "in_progress",
            "content": [
              {
                "type": "content",
                "content": {
                  "type": "text",
                  "text": "Found 3 configuration files..."
                }
              }
            ]
          }
        }
        """;

        var parser = new MessageParser();
        var updateParams = JsonSerializer.Deserialize<SessionUpdateParams>(json, parser.Options);

        Assert.NotNull(updateParams);
        var update = Assert.IsType<ToolCallStatusUpdate>(updateParams!.Update);
        Assert.Equal("call_001", update.ToolCallId);
        Assert.Equal(ToolCallStatus.InProgress, update.Status);
        Assert.Null(update.Title);
        Assert.Null(update.Kind);
        Assert.NotNull(update.Content);
        Assert.NotEmpty(update.Content!);
    }

    [Fact]
    public void Options_ShouldDeserialize_SessionLoadReplayChunks_OfficialPayloads()
    {
        var parser = new MessageParser();

        var userJson = """
        {
          "sessionId": "sess_789xyz",
          "update": {
            "sessionUpdate": "user_message_chunk",
            "content": {
              "type": "text",
              "text": "What's the capital of France?"
            }
          }
        }
        """;

        var agentJson = """
        {
          "sessionId": "sess_789xyz",
          "update": {
            "sessionUpdate": "agent_message_chunk",
            "content": {
              "type": "text",
              "text": "The capital of France is Paris."
            }
          }
        }
        """;

        var userUpdate = JsonSerializer.Deserialize<SessionUpdateParams>(userJson, parser.Options);
        var agentUpdate = JsonSerializer.Deserialize<SessionUpdateParams>(agentJson, parser.Options);

        var userMessage = Assert.IsType<UserMessageUpdate>(userUpdate!.Update);
        var userContent = Assert.IsType<TextContentBlock>(userMessage.Content);
        Assert.Equal("What's the capital of France?", userContent.Text);

        var agentMessage = Assert.IsType<AgentMessageUpdate>(agentUpdate!.Update);
        var agentContent = Assert.IsType<TextContentBlock>(agentMessage.Content);
        Assert.Equal("The capital of France is Paris.", agentContent.Text);
    }

    [Fact]
    public void Options_ShouldDeserialize_CurrentModeUpdate_OfficialPayload()
    {
        var json = """
        {
          "sessionId": "sess_mode",
          "update": {
            "sessionUpdate": "current_mode_update",
            "modeId": "code"
          }
        }
        """;

        var parser = new MessageParser();
        var updateParams = JsonSerializer.Deserialize<SessionUpdateParams>(json, parser.Options);

        Assert.NotNull(updateParams);
        var mode = Assert.IsType<CurrentModeUpdate>(updateParams!.Update);
        Assert.Equal("code", mode.LegacyModeId);
        Assert.Equal("code", mode.NormalizedModeId);
        Assert.Null(mode.Title);
    }

    [Fact]
    public void Options_ShouldDeserialize_ConfigOptionUpdate_OfficialPayload()
    {
        var json = """
        {
          "sessionId": "sess_config",
          "update": {
            "sessionUpdate": "config_option_update",
            "configOptions": [
              {
                "id": "mode",
                "name": "Session Mode",
                "type": "select",
                "currentValue": "code",
                "options": [
                  {
                    "value": "code",
                    "name": "Code"
                  },
                  {
                    "value": "plan",
                    "name": "Plan"
                  }
                ]
              }
            ]
          }
        }
        """;

        var parser = new MessageParser();
        var updateParams = JsonSerializer.Deserialize<SessionUpdateParams>(json, parser.Options);

        Assert.NotNull(updateParams);
        var config = Assert.IsType<ConfigOptionUpdate>(updateParams!.Update);
        Assert.Single(config.ConfigOptions!);
        Assert.Equal("mode", config.ConfigOptions![0].Id);
        Assert.Equal("code", config.ConfigOptions[0].CurrentValue);
    }

    [Fact]
    public void Options_ShouldDeserialize_AvailableCommandsUpdate_OfficialPayload()
    {
        var json = """
        {
          "sessionId": "sess_commands",
          "update": {
            "sessionUpdate": "available_commands_update",
            "availableCommands": [
              {
                "name": "web",
                "description": "Search the web for information",
                "input": {
                  "hint": "query to search for"
                }
              },
              {
                "name": "test",
                "description": "Run the project's tests",
                "input": {
                  "hint": "test command"
                }
              }
            ]
          }
        }
        """;

        var parser = new MessageParser();
        var updateParams = JsonSerializer.Deserialize<SessionUpdateParams>(json, parser.Options);

        Assert.NotNull(updateParams);
        var commands = Assert.IsType<AvailableCommandsUpdate>(updateParams!.Update);
        Assert.Equal(2, commands.AvailableCommands.Count);
        Assert.Equal("web", commands.AvailableCommands[0].Name);
        Assert.Equal("query to search for", commands.AvailableCommands[0].Input!.Hint);
    }

    [Fact]
    public void Options_ShouldDeserialize_SessionInfoUpdate_OfficialPartialPayload()
    {
        var json = """
        {
          "sessionId": "sess_info",
          "update": {
            "sessionUpdate": "session_info_update",
            "title": "Debug authentication timeout",
            "_meta": {
              "projectName": "api-server",
              "branch": "main"
            }
          }
        }
        """;

        var parser = new MessageParser();
        var updateParams = JsonSerializer.Deserialize<SessionUpdateParams>(json, parser.Options);

        Assert.NotNull(updateParams);
        var sessionInfo = Assert.IsType<SessionInfoUpdate>(updateParams!.Update);
        Assert.Equal("Debug authentication timeout", sessionInfo.Title);
        Assert.Null(sessionInfo.Description);
        Assert.Null(sessionInfo.Cwd);
        Assert.Null(sessionInfo.UpdatedAt);
        Assert.NotNull(sessionInfo.Meta);
        Assert.Equal("api-server", ReadMetaValue(sessionInfo.Meta!["projectName"]));
        Assert.Equal("main", ReadMetaValue(sessionInfo.Meta["branch"]));
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

