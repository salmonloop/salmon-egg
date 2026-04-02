using SalmonEgg.Domain.Models.Protocol;
using SalmonEgg.Infrastructure.Services;
using Xunit;

namespace SalmonEgg.Infrastructure.Tests.Services;

public sealed class CapabilityManagerTests
{
    [Fact]
    public void IsClientCapabilitySupported_ReturnsTrue_ForAskUserExtensionDeclaredInDefaults()
    {
        var manager = new CapabilityManager(ClientCapabilityDefaults.Create());

        Assert.True(manager.IsClientCapabilitySupported(ClientCapabilityMetadata.AskUserExtensionMethod));
        Assert.False(manager.IsClientCapabilitySupported(ClientCapabilityMetadata.LegacyAskUserExtensionMethod));
    }

    [Fact]
    public void DefaultManager_UsesSameDefaultCapabilityDeclarationAsInitializeFlow()
    {
        var manager = new CapabilityManager();
        var capabilities = manager.GetClientCapabilities();

        Assert.True(capabilities.SupportsExtension(ClientCapabilityMetadata.AskUserExtensionMethod));
        Assert.True(manager.IsCapabilitySupported(ClientCapabilityMetadata.AskUserExtensionMethod));
        Assert.False(capabilities.SupportsExtension(ClientCapabilityMetadata.LegacyAskUserExtensionMethod));
        Assert.False(manager.IsCapabilitySupported(ClientCapabilityMetadata.LegacyAskUserExtensionMethod));
        Assert.False(manager.IsClientCapabilitySupported("fs"));
        Assert.False(manager.IsClientCapabilitySupported("terminal"));
    }
}
