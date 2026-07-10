using System.Collections.Generic;
using Xunit;
using SalmonEgg.Acp.Protocol;

namespace SalmonEgg.Domain.Tests.Protocol;

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
}
