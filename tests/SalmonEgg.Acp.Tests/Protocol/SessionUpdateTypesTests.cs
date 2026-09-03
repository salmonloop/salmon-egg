using System;
using System.Text.Json;
using Xunit;
using SalmonEgg.Acp.Plan;
using SalmonEgg.Acp.Protocol;

namespace SalmonEgg.Acp.Tests.Protocol;

public sealed class SessionUpdateTypesTests
{
    [Fact]
    public void SessionUpdateParams_Update_RoundTripsAsSessionUpdatePayload()
    {
        var sessionParams = new SessionUpdateParams
        {
            SessionId = "test-session",
            Update = new CurrentModeUpdate { ModeId = "test-mode" }
        };

        var json = JsonSerializer.Serialize(sessionParams);
        var parsed = JsonSerializer.Deserialize<SessionUpdateParams>(json);

        Assert.NotNull(parsed);
        Assert.Equal("test-session", parsed!.SessionId);
        Assert.IsType<CurrentModeUpdate>(parsed.Update);
    }

    [Fact]
    public void SessionUpdateParams_Should_Serialize_With_Update()
    {
        // Given: A SessionUpdateParams with an update
        var sessionParams = new SessionUpdateParams
        {
            SessionId = "test-session",
            Update = new CurrentModeUpdate { ModeId = "test-mode" }
        };

        // When: Serialize to JSON
        var json = JsonSerializer.Serialize(sessionParams);
        var parsed = JsonDocument.Parse(json);

        // Then: update should be present in JSON
        Assert.True(parsed.RootElement.TryGetProperty("update", out var update));
        Assert.Equal(JsonValueKind.Object, update.ValueKind);
        Assert.Equal("test-mode", update.GetProperty("currentModeId").GetString());
        Assert.False(update.TryGetProperty("modeId", out _));
    }

    [Fact]
    public void ConfigOptionUpdate_ConfigOptions_RoundTrips()
    {
        var update = new ConfigOptionUpdate
        {
            ConfigOptions =
            [
                new ConfigOption
                {
                    Id = "mode",
                    Name = "Mode",
                    Type = "select",
                    CurrentValue = "plan",
                    Options =
                    [
                        new ConfigOptionValue
                        {
                            Value = "plan",
                            Name = "Plan"
                        }
                    ]
                }
            ]
        };

        var json = JsonSerializer.Serialize(update);
        var parsed = JsonSerializer.Deserialize<ConfigOptionUpdate>(json);

        Assert.NotNull(parsed);
        Assert.NotNull(parsed!.ConfigOptions);
        var option = Assert.Single(parsed.ConfigOptions!);
        Assert.Equal("mode", option.Id);
    }

    [Fact]
    public void PlanUpdate_Deserialization_PreservesStandardMetaFields()
    {
        var json = """
        {
          "sessionUpdate": "plan",
          "_meta": {
            "agent": "unit-test"
          },
          "entries": [
            {
              "_meta": {
                "id": "step-1"
              },
              "content": "Inspect plan contract",
              "priority": "high",
              "status": "pending"
            }
          ]
        }
        """;

        var parsed = JsonSerializer.Deserialize<PlanUpdate>(json);

        Assert.NotNull(parsed);
        Assert.NotNull(parsed!.Meta);
        Assert.Equal("unit-test", ReadMetaValue(parsed.Meta!["agent"]));

        var entry = Assert.Single(parsed.Entries);
        Assert.NotNull(entry.Meta);
        Assert.Equal("step-1", ReadMetaValue(entry.Meta!["id"]));
    }

    /// <summary>
    /// 协议宽松度：<c>Plan.entries</c> 标了 x-deserialize-skip-invalid-items，
    /// 因此 <c>content: null</c> 让这一条 entry 读不出来时，必须只丢这一条、保留其余，
    /// 不得让整条 plan 更新失败。<c>PlanEntry.content</c> 自身仍是必填且无容忍标注 ——
    /// 它抛出的异常正是「该元素无效」的判据，由数组层吸收。
    /// </summary>
    [Fact]
    public void PlanUpdate_Deserialization_SkipsEntryWithNullContent()
    {
        var json = """
        {
          "sessionUpdate": "plan",
          "entries": [
            {
              "content": null,
              "priority": "high",
              "status": "pending"
            },
            {
              "content": "survivor",
              "priority": "low",
              "status": "pending"
            }
          ]
        }
        """;

        var update = Assert.IsType<PlanUpdate>(JsonSerializer.Deserialize<PlanUpdate>(json));

        var entry = Assert.Single(update.Entries);
        Assert.Equal("survivor", entry.Content);
    }

    /// <summary>
    /// 协议宽松度：<c>Plan.entries</c> 同时标了 x-deserialize-default-on-error，
    /// 因此 <c>entries: null</c> 回落为空列表而不是抛错。
    /// </summary>
    [Fact]
    public void PlanUpdate_Deserialization_NullEntriesDegradesToEmpty()
    {
        var json = """
        {
          "sessionUpdate": "plan",
          "entries": null
        }
        """;

        var update = Assert.IsType<PlanUpdate>(JsonSerializer.Deserialize<PlanUpdate>(json));

        Assert.Empty(update.Entries);
    }

    /// <summary>
    /// 协议宽松度：数组里的 null 元素按 skip-invalid-items 跳过。
    /// </summary>
    [Fact]
    public void PlanUpdate_Deserialization_SkipsNullEntryItem()
    {
        var json = """
        {
          "sessionUpdate": "plan",
          "entries": [null]
        }
        """;

        var update = Assert.IsType<PlanUpdate>(JsonSerializer.Deserialize<PlanUpdate>(json));

        Assert.Empty(update.Entries);
    }

    private static string? ReadMetaValue(object? value)
    {
        return value switch
        {
            null => null,
            JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString(),
            JsonElement element => element.GetRawText(),
            _ => value.ToString()
        };
    }
}
