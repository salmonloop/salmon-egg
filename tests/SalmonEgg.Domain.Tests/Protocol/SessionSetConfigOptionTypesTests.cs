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
}
