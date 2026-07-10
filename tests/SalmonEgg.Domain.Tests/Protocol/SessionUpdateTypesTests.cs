using System;
using System.Text.Json;
using Xunit;
using SalmonEgg.Acp.Plan;
using SalmonEgg.Acp.Protocol;

namespace SalmonEgg.Domain.Tests.Protocol;

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
            ConfigOptions = [new ConfigOption { Id = "mode", Name = "Mode", Type = "select" }]
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

    [Fact]
    public void PlanUpdate_Deserialization_RejectsNullEntryContent()
    {
        var json = """
        {
          "sessionUpdate": "plan",
          "entries": [
            {
              "content": null,
              "priority": "high",
              "status": "pending"
            }
          ]
        }
        """;

        Assert.Throws<JsonException>((Action)(() => JsonSerializer.Deserialize<PlanUpdate>(json)));
    }

    [Fact]
    public void PlanUpdate_Deserialization_RejectsNullEntries()
    {
        var json = """
        {
          "sessionUpdate": "plan",
          "entries": null
        }
        """;

        Assert.Throws<JsonException>((Action)(() => JsonSerializer.Deserialize<PlanUpdate>(json)));
    }

    [Fact]
    public void PlanUpdate_Deserialization_RejectsNullEntryItem()
    {
        var json = """
        {
          "sessionUpdate": "plan",
          "entries": [null]
        }
        """;

        Assert.Throws<JsonException>((Action)(() => JsonSerializer.Deserialize<PlanUpdate>(json)));
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
