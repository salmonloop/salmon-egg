using System.Collections.Generic;
using System.Text.Json;
using Xunit;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Acp.Serialization;

namespace SalmonEgg.Acp.Tests.Protocol;

public sealed class InitializeTypesTests
{
    [Fact]
    public void AgentCapabilities_Should_Not_Report_Missing_Standard_Session_Capabilities_As_Supported()
    {
        var capabilities = new AgentCapabilities();

        Assert.False(capabilities.SupportsSessionLoading);
        Assert.False(capabilities.SupportsSessionResume);
        Assert.False(capabilities.SupportsSessionClose);
        Assert.False(capabilities.SupportsSessionList);
    }

    [Fact]
    public void ClientCapabilityDefaults_Should_Not_Advertise_FileSystem_Or_Terminal()
    {
        var capabilities = ClientCapabilityDefaults.Create();

        Assert.Null(capabilities.Terminal);
        Assert.Null(capabilities.Fs);
    }

    [Fact]
    public void ClientCapabilityDefaults_Should_Advertise_AskUser_Extension_In_Meta()
    {
        var capabilities = ClientCapabilityDefaults.Create();

        Assert.NotNull(capabilities.Meta);
        Assert.True(capabilities.Meta!.TryGetValue(ClientCapabilityMetadata.ExtensionsMetaKey, out var extensions));
        var extensionMap = Assert.IsType<Dictionary<string, object?>>(extensions);
        Assert.True(extensionMap.TryGetValue(ClientCapabilityMetadata.AskUserExtensionMethod, out var isSupported));
        Assert.Equal(true, isSupported);
        Assert.False(extensionMap.TryGetValue("interaction.ask_user", out _));
    }

    [Fact]
    public void ClientCapabilities_SupportsExtension_Should_Return_True_For_Default_AskUser_Metadata()
    {
        var capabilities = ClientCapabilityDefaults.Create();

        Assert.True(capabilities.SupportsExtension(ClientCapabilityMetadata.AskUserExtensionMethod));
        Assert.False(capabilities.SupportsExtension("interaction.ask_user"));
    }

    // ACP v2 规范锚点:「Agents that support the session method surface MUST support
    // session/list / session/resume / session/close」——基线方法由 session 对象本身的
    // presence 隐含,不存在 session.list/resume/close 嵌套标记;不得反向收紧为逐项门控。
    // https://agentclientprotocol.com/protocol/v2/session-setup.md
    [Fact]
    public void InitializeResponseV2_EmptySessionSurface_ImpliesBaselineListResumeClose()
    {
        var response = JsonSerializer.Deserialize(
            """
            {
              "protocolVersion": 2,
              "info": { "name": "agent", "version": "1.0.0" },
              "capabilities": { "session": {} }
            }
            """,
            AcpJsonContext.Default.InitializeResponse);

        Assert.NotNull(response);
        var capabilities = response!.AgentCapabilities;
        Assert.True(capabilities.SupportsSessionList);
        Assert.True(capabilities.SupportsSessionResume);
        Assert.True(capabilities.SupportsSessionClose);
        Assert.False(capabilities.SupportsSessionDelete);
        Assert.False(capabilities.SupportsSessionAdditionalDirectories);
        Assert.False(capabilities.SupportsSessionLoading);
    }

    [Fact]
    public void InitializeResponseV2_DeleteMarkerPresent_GatesDeleteWithoutNarrowingBaseline()
    {
        var response = JsonSerializer.Deserialize(
            """
            {
              "protocolVersion": 2,
              "info": { "name": "agent", "version": "1.0.0" },
              "capabilities": { "session": { "delete": {} } }
            }
            """,
            AcpJsonContext.Default.InitializeResponse);

        Assert.NotNull(response);
        var capabilities = response!.AgentCapabilities;
        Assert.True(capabilities.SupportsSessionDelete);
        Assert.True(capabilities.SupportsSessionList);
        Assert.True(capabilities.SupportsSessionResume);
        Assert.True(capabilities.SupportsSessionClose);
    }

    [Fact]
    public void InitializeResponseV2_MissingSessionSurface_ReportsSessionMethodsUnsupported()
    {
        var response = JsonSerializer.Deserialize(
            """
            {
              "protocolVersion": 2,
              "info": { "name": "agent", "version": "1.0.0" },
              "capabilities": {}
            }
            """,
            AcpJsonContext.Default.InitializeResponse);

        Assert.NotNull(response);
        var capabilities = response!.AgentCapabilities;
        Assert.False(capabilities.SupportsSessionList);
        Assert.False(capabilities.SupportsSessionResume);
        Assert.False(capabilities.SupportsSessionClose);
        Assert.False(capabilities.SupportsSessionDelete);
    }

    [Fact]
    public void AgentCapabilities_V2SessionSurface_DoesNotImplyLoadSession()
    {
        var capabilities = new AgentCapabilities(
            sessionCapabilities: new SessionCapabilities
            {
                List = new SessionListCapabilities(),
                Resume = new SessionResumeCapabilities(),
                Close = new SessionCloseCapabilities()
            });

        Assert.False(capabilities.SupportsSessionLoading);
        Assert.True(capabilities.SupportsSessionList);
        Assert.True(capabilities.SupportsSessionResume);
        Assert.True(capabilities.SupportsSessionClose);
    }
}
