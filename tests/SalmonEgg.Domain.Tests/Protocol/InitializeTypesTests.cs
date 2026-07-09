using System.Collections.Generic;
using NUnit.Framework;
using SalmonEgg.Acp.Protocol;

namespace SalmonEgg.Domain.Tests.Protocol;

[TestFixture]
public sealed class InitializeTypesTests
{
    [Test]
    public void AgentCapabilities_Should_Not_Report_Missing_Standard_Session_Capabilities_As_Supported()
    {
        var capabilities = new AgentCapabilities();

        Assert.That(capabilities.SupportsSessionLoading, Is.False);
        Assert.That(capabilities.SupportsSessionResume, Is.False);
        Assert.That(capabilities.SupportsSessionClose, Is.False);
        Assert.That(capabilities.SupportsSessionList, Is.False);
    }

    [Test]
    public void ClientCapabilityDefaults_Should_Not_Advertise_FileSystem_Or_Terminal()
    {
        var capabilities = ClientCapabilityDefaults.Create();

        Assert.That(capabilities.Terminal, Is.Null);
        Assert.That(capabilities.Fs, Is.Null);
    }

    [Test]
    public void ClientCapabilityDefaults_Should_Advertise_AskUser_Extension_In_Meta()
    {
        var capabilities = ClientCapabilityDefaults.Create();

        Assert.That(capabilities.Meta, Is.Not.Null);
        Assert.That(capabilities.Meta!.TryGetValue(ClientCapabilityMetadata.ExtensionsMetaKey, out var extensions), Is.True);
        Assert.That(extensions, Is.TypeOf<Dictionary<string, object?>>());

        var extensionMap = (Dictionary<string, object?>)extensions!;
        Assert.That(extensionMap.TryGetValue(ClientCapabilityMetadata.AskUserExtensionMethod, out var isSupported), Is.True);
        Assert.That(isSupported, Is.EqualTo(true));
        Assert.That(extensionMap.TryGetValue("interaction.ask_user", out _), Is.False);
    }

    [Test]
    public void ClientCapabilities_SupportsExtension_Should_Return_True_For_Default_AskUser_Metadata()
    {
        var capabilities = ClientCapabilityDefaults.Create();

        Assert.That(
            capabilities.SupportsExtension(ClientCapabilityMetadata.AskUserExtensionMethod),
            Is.True);
        Assert.That(
            capabilities.SupportsExtension("interaction.ask_user"),
            Is.False);
    }
}
