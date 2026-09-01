using System.Text.Json;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Acp.Serialization;
using Xunit;

namespace SalmonEgg.Acp.Tests.Protocol;

public sealed class SessionPromptV2Tests
{
    [Fact]
    public void V2PromptResponse_IsBareAcknowledgement_NotTerminalResult()
    {
        var parsed = JsonSerializer.Deserialize("{}", AcpJsonContext.Default.SessionPromptResponse);
        Assert.False(parsed!.HasStopReason);
        Assert.Equal(StopReason.EndTurn, parsed.StopReason);
        string v2;
        using (AcpProtocolWriteContext.Enter(AcpProtocolVersion.V2))
            v2 = JsonSerializer.Serialize(parsed, AcpJsonContext.Default.SessionPromptResponse);
        Assert.Equal("{}", v2);
    }

    [Fact]
    public void V1PromptResponse_PreservesStopReason()
    {
        var parsed = JsonSerializer.Deserialize("{\"stopReason\":\"cancelled\"}", AcpJsonContext.Default.SessionPromptResponse);
        Assert.True(parsed!.HasStopReason);
        Assert.Equal(StopReason.Cancelled, parsed.StopReason);
        Assert.Contains("\"stopReason\":\"cancelled\"", JsonSerializer.Serialize(parsed, AcpJsonContext.Default.SessionPromptResponse), StringComparison.Ordinal);
    }
}
