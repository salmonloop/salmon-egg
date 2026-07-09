using System.Text.Json;
using NUnit.Framework;
using SalmonEgg.Acp.Protocol;

namespace SalmonEgg.Domain.Tests.Protocol;

[TestFixture]
public sealed class SessionSetConfigOptionTypesTests
{
    [Test]
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

        Assert.That(parsed, Is.Not.Null);
        Assert.That(parsed!.Value, Is.EqualTo("test-value"));
    }

    [Test]
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
        Assert.That(parsed.RootElement.TryGetProperty("value", out var value), Is.True);
        Assert.That(value.ValueKind, Is.EqualTo(JsonValueKind.String));
        Assert.That(value.GetString(), Is.EqualTo("test-value"));
    }
}
