using System.Text.Json;
using Xunit;
using SalmonEgg.Acp.Protocol;

namespace SalmonEgg.Acp.Tests.Protocol;

public sealed class SessionSetModeTypesTests
{
    [Fact]
    public void SessionSetModeParams_Should_Serialize_OfficialRequestFields()
    {
        var parameters = new SessionSetModeParams("session-1", "code");

        var json = JsonSerializer.Serialize(parameters);
        using var parsed = JsonDocument.Parse(json);

        Assert.Equal("session-1", parsed.RootElement.GetProperty("sessionId").GetString());
        Assert.Equal("code", parsed.RootElement.GetProperty("modeId").GetString());
    }

    [Fact]
    public void SessionSetModeResponse_Should_Serialize_WithoutNonStandardModeId()
    {
        var response = new SessionSetModeResponse();

        var json = JsonSerializer.Serialize(response);
        using var parsed = JsonDocument.Parse(json);

        Assert.False(parsed.RootElement.TryGetProperty("modeId", out _));
    }
}
