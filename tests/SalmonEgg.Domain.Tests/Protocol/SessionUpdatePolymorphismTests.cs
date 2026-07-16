using System.Text.Json;
using Xunit;
using SalmonEgg.Acp.Protocol;

namespace SalmonEgg.Domain.Tests.Protocol;

public sealed class SessionUpdatePolymorphismTests
{
    [Fact]
    public void Deserialize_CurrentModeUpdate_Works()
    {
        var json = """
        {
          "sessionId": "s1",
          "update": {
            "sessionUpdate": "current_mode_update",
            "currentModeId": "mode_123",
            "title": "non-standard title"
          }
        }
        """;

        var parsed = JsonSerializer.Deserialize<SessionUpdateParams>(json, CreateJsonOptions());

        Assert.NotNull(parsed);
        Assert.Equal("s1", parsed!.SessionId);
        Assert.IsType<CurrentModeUpdate>(parsed.Update);

        var update = (CurrentModeUpdate)parsed.Update!;
        Assert.Equal("mode_123", update.ModeId);
        Assert.Null(typeof(CurrentModeUpdate).GetProperty("Title"));
        var serialized = JsonSerializer.Serialize(parsed, CreateJsonOptions());
        Assert.DoesNotContain("title", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void Deserialize_NonStandardConfigOptionsUpdate_FallsBackWithoutReplayingPayload()
    {
        var json = """
        {
          "sessionId": "s1",
          "update": {
            "sessionUpdate": "config_options_update",
            "configOptions": []
          }
        }
        """;

        var parsed = JsonSerializer.Deserialize<SessionUpdateParams>(json, CreateJsonOptions());

        Assert.NotNull(parsed);
        Assert.IsType<SessionUpdate>(parsed!.Update);
        var serialized = JsonSerializer.Serialize(parsed, CreateJsonOptions());
        Assert.DoesNotContain("config_options_update", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("configOptions", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void Deserialize_ConfigOptionUpdate_Works()
    {
        var json = """
        {
          "sessionId": "s1",
          "update": {
            "sessionUpdate": "config_option_update",
            "configOptions": [
              {
                "id": "mode",
                "name": "Mode",
                "category": "mode",
                "type": "select",
                "currentValue": "agent",
                "options": [
                  { "value": "agent", "name": "Agent" }
                ]
              }
            ]
          }
        }
        """;

        var parsed = JsonSerializer.Deserialize<SessionUpdateParams>(json, CreateJsonOptions());

        Assert.NotNull(parsed);
        Assert.IsType<ConfigOptionUpdate>(parsed!.Update);

        var update = (ConfigOptionUpdate)parsed.Update!;
        Assert.NotNull(update.ConfigOptions);
        Assert.NotEmpty(update.ConfigOptions!);
        Assert.Equal("mode", update.ConfigOptions![0].Id);
        Assert.Equal("agent", update.ConfigOptions[0].CurrentValue);
    }

    [Fact]
    public void Deserialize_SessionInfoUpdate_WithOfficialFields_Works()
    {
        var json = """
        {
          "sessionId": "s-info",
          "update": {
            "sessionUpdate": "session_info_update",
            "title": "New Title",
            "updatedAt": "2026-03-22T19:00:00Z",
            "_meta": {
              "source": "unit-test",
              "pinned": true,
              "rank": 3
            }
          }
        }
        """;

        var parsed = JsonSerializer.Deserialize<SessionUpdateParams>(json, CreateJsonOptions());

        Assert.NotNull(parsed);
        Assert.IsType<SessionInfoUpdate>(parsed!.Update);

        var update = (SessionInfoUpdate)parsed.Update!;
        Assert.Equal("New Title", update.Title);
        Assert.True(update.HasTitle);
        Assert.Equal("2026-03-22T19:00:00Z", update.UpdatedAt);
        Assert.True(update.HasUpdatedAt);

        var meta = update.Meta;
        Assert.NotNull(meta);
        Assert.True(meta!.ContainsKey("source"));
        Assert.Equal("unit-test", ReadMetaValue(meta["source"]));
        Assert.Equal("true", ReadMetaValue(meta["pinned"]));
        Assert.Equal("3", ReadMetaValue(meta["rank"]));
    }

    [Fact]
    public void Deserialize_SessionInfoUpdate_IgnoresUnsupportedCwdField()
    {
        var json = """
        {
          "sessionId": "s-info",
          "update": {
            "sessionUpdate": "session_info_update",
            "cwd": "/home/user/project",
            "title": "New Title"
          }
        }
        """;

        var serializerOptions = CreateJsonOptions();
        var parsed = JsonSerializer.Deserialize<SessionUpdateParams>(json, serializerOptions);

        Assert.NotNull(parsed);
        Assert.IsType<SessionInfoUpdate>(parsed!.Update);

        Assert.Null(typeof(SessionInfoUpdate).GetProperty("Cwd"));
    }

    [Fact]
    public void Deserialize_SessionInfoUpdate_AllowsPartialPayloads()
    {
        var json = """
        {
          "sessionId": "s-info",
          "update": {
            "sessionUpdate": "session_info_update",
            "_meta": {
              "source": "unit-test"
            }
          }
        }
        """;

        var parsed = JsonSerializer.Deserialize<SessionUpdateParams>(json, CreateJsonOptions());

        Assert.NotNull(parsed);
        Assert.IsType<SessionInfoUpdate>(parsed!.Update);

        var update = (SessionInfoUpdate)parsed.Update!;
        Assert.Null(update.Title);
        Assert.False(update.HasTitle);
        Assert.Null(update.UpdatedAt);
        Assert.False(update.HasUpdatedAt);

        var meta = update.Meta;
        Assert.NotNull(meta);
        Assert.True(meta!.ContainsKey("source"));
        Assert.Equal("unit-test", ReadMetaValue(meta["source"]));
    }

    [Fact]
    public void Deserialize_SessionInfoUpdate_WithNullTitle_MarksTitleAsPresent()
    {
        var json = """
        {
          "sessionId": "s-info",
          "update": {
            "sessionUpdate": "session_info_update",
            "title": null
          }
        }
        """;

        var parsed = JsonSerializer.Deserialize<SessionUpdateParams>(json, CreateJsonOptions());

        Assert.NotNull(parsed);
        Assert.IsType<SessionInfoUpdate>(parsed!.Update);

        var update = (SessionInfoUpdate)parsed.Update!;
        Assert.Null(update.Title);
        Assert.True(update.HasTitle);
    }

    [Fact]
    public void Deserialize_SessionInfoUpdate_WithNullUpdatedAt_MarksUpdatedAtAsPresent()
    {
        var json = """
        {
          "sessionId": "s-info",
          "update": {
            "sessionUpdate": "session_info_update",
            "updatedAt": null
          }
        }
        """;

        var parsed = JsonSerializer.Deserialize<SessionUpdateParams>(json, CreateJsonOptions());

        Assert.NotNull(parsed);
        Assert.IsType<SessionInfoUpdate>(parsed!.Update);

        var update = (SessionInfoUpdate)parsed.Update!;
        Assert.Null(update.UpdatedAt);
        Assert.True(update.HasUpdatedAt);
    }

    [Fact]
    public void Deserialize_CurrentModeUpdate_WithNonStandardModeId_DoesNotPopulateModeId()
    {
        var json = """
        {
          "sessionId": "s1",
          "update": {
            "sessionUpdate": "current_mode_update",
            "modeId": "non-standard-mode"
          }
        }
        """;

        var parsed = JsonSerializer.Deserialize<SessionUpdateParams>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(parsed);
        Assert.IsType<CurrentModeUpdate>(parsed!.Update);

        var update = (CurrentModeUpdate)parsed.Update!;
        Assert.Empty(update.ModeId);
    }

    [Fact]
    public void Deserialize_ToolCallStatusUpdate_WithExtendedSchemaFields_Works()
    {
        var json = """
        {
          "sessionId": "s1",
          "update": {
            "sessionUpdate": "tool_call_update",
            "toolCallId": "call-1",
            "title": "Switch mode",
            "kind": "switch_mode",
            "status": "completed",
            "rawInput": { "targetMode": "plan" },
            "rawOutput": { "applied": true }
          }
        }
        """;

        var parsed = JsonSerializer.Deserialize<SessionUpdateParams>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(parsed);
        Assert.IsType<ToolCallStatusUpdate>(parsed!.Update);

        var update = (ToolCallStatusUpdate)parsed.Update!;
        Assert.Equal("call-1", update.ToolCallId);
        Assert.Equal("Switch mode", update.Title);
        Assert.Equal(SalmonEgg.Acp.Tool.ToolCallKind.SwitchMode, update.Kind);
        Assert.Equal(SalmonEgg.Acp.Tool.ToolCallStatus.Completed, update.Status);
        Assert.True(update.RawInput.HasValue);
        Assert.True(update.RawOutput.HasValue);
        var rawInput = update.RawInput.GetValueOrDefault();
        var rawOutput = update.RawOutput.GetValueOrDefault();
        Assert.Equal("plan", rawInput.GetProperty("targetMode").GetString());
        Assert.True(rawOutput.GetProperty("applied").GetBoolean());
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        return new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
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
