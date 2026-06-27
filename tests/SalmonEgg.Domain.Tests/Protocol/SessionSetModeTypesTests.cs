using System.Text.Json;
using NUnit.Framework;
using SalmonEgg.Domain.Models.Protocol;

namespace SalmonEgg.Domain.Tests.Protocol;

[TestFixture]
public sealed class SessionSetModeTypesTests
{
    [Test]
    public void SessionSetModeParams_Should_Serialize_OfficialRequestFields()
    {
        var parameters = new SessionSetModeParams("session-1", "code");

        var json = JsonSerializer.Serialize(parameters);
        using var parsed = JsonDocument.Parse(json);

        Assert.That(parsed.RootElement.GetProperty("sessionId").GetString(), Is.EqualTo("session-1"));
        Assert.That(parsed.RootElement.GetProperty("modeId").GetString(), Is.EqualTo("code"));
    }

    [Test]
    public void SessionSetModeResponse_Should_Serialize_WithoutNonStandardModeId()
    {
        var response = new SessionSetModeResponse();

        var json = JsonSerializer.Serialize(response);
        using var parsed = JsonDocument.Parse(json);

        Assert.That(parsed.RootElement.TryGetProperty("modeId", out _), Is.False);
    }
}
