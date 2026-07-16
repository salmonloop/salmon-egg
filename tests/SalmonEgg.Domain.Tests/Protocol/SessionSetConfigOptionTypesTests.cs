using System.Text.Json;
using Xunit;
using SalmonEgg.Acp.Protocol;

namespace SalmonEgg.Domain.Tests.Protocol;

public sealed class SessionSetConfigOptionTypesTests
{
    [Fact]
    public void SessionSetConfigOptionParams_Value_Should_Deserialize_As_String()
    {
        var json = """
        {
          "sessionId": "test-session",
          "configId": "test-config",
          "value": "test-value"
        }
        """;

        var parsed = JsonSerializer.Deserialize<SessionSetConfigOptionParams>(json);

        Assert.NotNull(parsed);
        Assert.Equal("test-value", parsed!.Value);
    }

    [Fact]
    public void SessionSetConfigOptionParams_Should_Serialize_Value_As_String()
    {
        // Given: A SessionSetConfigOptionParams with a value
        var sessionParams = new SessionSetConfigOptionParams
        {
            SessionId = "test-session",
            ConfigId = "test-config",
            Value = "test-value"
        };

        // When: Serialize to JSON
        var json = JsonSerializer.Serialize(sessionParams);
        var parsed = JsonDocument.Parse(json);

        // Then: value should be a string in JSON
        Assert.True(parsed.RootElement.TryGetProperty("value", out var value));
        Assert.Equal(JsonValueKind.String, value.ValueKind);
        Assert.Equal("test-value", value.GetString());
    }

    [Fact]
    public void SessionSetConfigOptionParams_BooleanVariant_Should_RoundTrip_WithOfficialDiscriminator()
    {
        var original = new SessionSetConfigOptionParams("test-session", "auto-approve", true)
        {
            Meta = new Dictionary<string, object?>
            {
                ["source"] = "unit-test"
            }
        };

        var json = JsonSerializer.Serialize(original);
        var parsed = JsonSerializer.Deserialize<SessionSetConfigOptionParams>(json);
        using var document = JsonDocument.Parse(json);

        Assert.Equal("boolean", document.RootElement.GetProperty("type").GetString());
        Assert.True(document.RootElement.GetProperty("value").GetBoolean());
        Assert.Equal("unit-test", document.RootElement.GetProperty("_meta").GetProperty("source").GetString());
        Assert.NotNull(parsed);
        Assert.Null(parsed!.Value);
        Assert.True(parsed.BooleanValue);
        Assert.NotNull(parsed.Meta);
    }

    [Fact]
    public void SessionSetConfigOptionParams_StringVariant_WithUnknownType_DeserializesAsValueId()
    {
        var json = """
        {
          "sessionId": "test-session",
          "configId": "mode",
          "type": "future-select",
          "value": "plan"
        }
        """;

        var parsed = JsonSerializer.Deserialize<SessionSetConfigOptionParams>(json);

        Assert.NotNull(parsed);
        Assert.Equal("plan", parsed!.Value);
        Assert.Null(parsed.BooleanValue);
    }
}
