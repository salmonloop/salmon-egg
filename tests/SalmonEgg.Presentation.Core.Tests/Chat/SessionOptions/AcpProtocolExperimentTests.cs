using System.Text.Json;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Acp.Serialization;
using SalmonEgg.Application.Services.Acp;
using SalmonEgg.Presentation.Core.Services.Chat;

namespace SalmonEgg.Presentation.Core.Tests.Chat.SessionOptions;

public sealed class AcpProtocolExperimentTests
{
    [Fact]
    public void DefaultPolicy_OffersOnlyStableProtocol()
    {
        var policy = new AcpProtocolExperimentPolicy();
        var request = AcpInitializeRequestFactory.CreateDefault();

        Assert.Equal(AcpProtocolVersion.V1, policy.OfferedProtocolVersion);
        Assert.Equal(AcpProtocolVersion.V1, request.ProtocolVersion);
        Assert.NotNull(request.ClientCapabilities.Session);
    }

    [Fact]
    public void ExplicitPolicy_UsesDraftInitializeShapeWithoutLegacyCapabilities()
    {
        var policy = new AcpProtocolExperimentPolicy(EnableDraftV2: true);
        var request = AcpInitializeRequestFactory.CreateDefault(protocolVersion: policy.OfferedProtocolVersion);

        var wire = JsonSerializer.SerializeToElement(request, AcpJsonContext.Default.InitializeParams);

        Assert.Equal(AcpProtocolVersion.V2, request.ProtocolVersion);
        Assert.True(wire.TryGetProperty("info", out _));
        Assert.False(wire.TryGetProperty("clientInfo", out _));
        var capabilities = wire.GetProperty("capabilities");
        Assert.False(capabilities.TryGetProperty("session", out _));
        Assert.False(capabilities.TryGetProperty("fs", out _));
        Assert.False(capabilities.TryGetProperty("terminal", out _));
    }

    [Fact]
    public void DraftColdHydration_UsesFullHistoryIntentWithResumeCapability()
    {
        var capabilities = new AgentCapabilities
        {
            SessionCapabilities = new SessionCapabilities { Resume = new SessionResumeCapabilities() }
        };

        var draft = AcpSessionRecoveryPolicy.ResolveForHydration(capabilities, AcpProtocolVersion.V2);
        var stable = AcpSessionRecoveryPolicy.ResolveForHydration(capabilities, AcpProtocolVersion.V1);

        Assert.Equal(AcpSessionRecoveryMode.Load, draft);
        Assert.True(AcpSessionRecoveryPolicy.ExpectsHistoryReplayForHydration(draft));
        Assert.Equal(AcpSessionRecoveryMode.None, stable);
    }
}
