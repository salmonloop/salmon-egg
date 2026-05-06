using System.Collections.Generic;
using System.Text.Json;
using NUnit.Framework;
using SalmonEgg.Domain.Models.Protocol;

namespace SalmonEgg.Domain.Tests.Protocol;

[TestFixture]
public sealed class InitializeTypesTests
{
    [Test]
    public void InitializeParams_ProtocolVersion_ShouldBe_Integer()
    {
        // Given: An InitializeParams with protocol version
        var initializeParams = new InitializeParams
        {
            ProtocolVersion = 1
        };

        // When: Serialize to JSON
        var json = JsonSerializer.Serialize(initializeParams);
        var parsed = JsonDocument.Parse(json);

        // Then: ProtocolVersion should be an integer in JSON
        Assert.That(parsed.RootElement.GetProperty("protocolVersion").ValueKind, Is.EqualTo(JsonValueKind.Number));
    }

    [Test]
    public void InitializeParams_ProtocolVersion_ShouldNotBe_String()
    {
        // Given: An InitializeParams with protocol version
        var initializeParams = new InitializeParams
        {
            ProtocolVersion = 1
        };

        // When: Serialize to JSON
        var json = JsonSerializer.Serialize(initializeParams);

        // Then: JSON should not contain string version
        Assert.That(json, Does.Not.Contain("\"2024-11-05\""));
        Assert.That(json, Does.Contain("\"protocolVersion\":1"));
    }

    [Test]
    public void InitializeResponse_ProtocolVersion_Should_Serialize_As_Integer()
    {
        // Given: An InitializeResponse with protocol version
        var response = new InitializeResponse
        {
            ProtocolVersion = 1
        };

        // When: Serialize to JSON
        var json = JsonSerializer.Serialize(response);
        var parsed = JsonDocument.Parse(json);

        // Then: ProtocolVersion should be an integer in JSON
        Assert.That(parsed.RootElement.GetProperty("protocolVersion").ValueKind, Is.EqualTo(JsonValueKind.Number));
    }

    [Test]
    public void InitializeResponse_ProtocolVersion_ShouldNotBe_String()
    {
        // Given: An InitializeResponse with protocol version
        var response = new InitializeResponse
        {
            ProtocolVersion = 1
        };

        // When: Serialize to JSON
        var json = JsonSerializer.Serialize(response);

        // Then: JSON should not contain string version
        Assert.That(json, Does.Not.Contain("\"2024-11-05\""));
        Assert.That(json, Does.Contain("\"protocolVersion\":1"));
    }

    [Test]
    public void AgentCapabilities_SessionCapabilities_Should_Serialize_Correctly()
    {
        // Given: An AgentCapabilities with session capabilities
        var capabilities = new AgentCapabilities
        {
            SessionCapabilities = new SessionCapabilities
            {
                List = new SessionListCapabilities()
            }
        };

        // When: Serialize to JSON
        var json = JsonSerializer.Serialize(capabilities);
        var parsed = JsonDocument.Parse(json);

        // Then: sessionCapabilities should be present in JSON
        Assert.That(parsed.RootElement.TryGetProperty("sessionCapabilities", out var sessionCaps), Is.True);
        Assert.That(sessionCaps.TryGetProperty("list", out _), Is.True);
    }

    [Test]
    public void AgentCapabilities_Should_Deserialize_Standard_Session_Capability_Objects()
    {
        var json = """
                   {
                     "sessionCapabilities": {
                       "resume": {},
                       "close": {},
                       "list": {}
                     }
                   }
                   """;

        var capabilities = JsonSerializer.Deserialize<AgentCapabilities>(json);

        Assert.That(capabilities, Is.Not.Null);
        Assert.That(capabilities!.SessionCapabilities, Is.Not.Null);
        Assert.That(capabilities.SessionCapabilities!.List, Is.Not.Null);
        Assert.That(capabilities.SessionCapabilities.Resume, Is.Not.Null);
        Assert.That(capabilities.SessionCapabilities.Close, Is.Not.Null);
    }

    [Test]
    public void AgentCapabilities_Should_Report_Standard_Session_Capability_Support()
    {
        var json = """
                   {
                     "loadSession": true,
                     "sessionCapabilities": {
                       "resume": {},
                       "close": {},
                       "list": {}
                     }
                   }
                   """;

        var capabilities = JsonSerializer.Deserialize<AgentCapabilities>(json);

        Assert.That(capabilities, Is.Not.Null);
        Assert.That(capabilities!.SupportsSessionLoading, Is.True);
        Assert.That(capabilities.SupportsSessionResume, Is.True);
        Assert.That(capabilities.SupportsSessionClose, Is.True);
        Assert.That(capabilities.SupportsSessionList, Is.True);
    }

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
    public void InitializeParams_Meta_Should_Serialize_With_UnderscoreMeta()
    {
        // Given: InitializeParams with _meta
        var initializeParams = new InitializeParams
        {
            Meta = new Dictionary<string, object?>
            {
                ["foo"] = "bar",
                ["count"] = 3,
                ["nullValue"] = null
            }
        };

        // When: Serialize to JSON
        var json = JsonSerializer.Serialize(initializeParams);
        var parsed = JsonDocument.Parse(json);

        // Then: _meta should be present and correctly typed
        Assert.That(parsed.RootElement.TryGetProperty("_meta", out var meta), Is.True);
        Assert.That(meta.GetProperty("foo").ValueKind, Is.EqualTo(JsonValueKind.String));
        Assert.That(meta.GetProperty("count").ValueKind, Is.EqualTo(JsonValueKind.Number));
        Assert.That(meta.GetProperty("nullValue").ValueKind, Is.EqualTo(JsonValueKind.Null));
    }

    [Test]
    public void InitializeResponse_Meta_Should_Deserialize_Correctly()
    {
        // Given: JSON with _meta
        var json = """
                   {
                     "protocolVersion": 1,
                     "agentInfo": { "name": "agent", "version": "1.0.0" },
                     "agentCapabilities": {},
                     "_meta": { "source": "unit-test", "flag": true }
                   }
                   """;

        // When: Deserialize from JSON
        var response = JsonSerializer.Deserialize<InitializeResponse>(json);

        // Then: _meta should be present with JsonElement values
        Assert.That(response, Is.Not.Null);
        Assert.That(response!.Meta, Is.Not.Null);
        Assert.That(response.Meta!.ContainsKey("source"), Is.True);
        Assert.That(response.Meta["source"], Is.TypeOf<JsonElement>());
        Assert.That(((JsonElement)response.Meta["source"]!).GetString(), Is.EqualTo("unit-test"));
        Assert.That(((JsonElement)response.Meta["flag"]!).ValueKind, Is.EqualTo(JsonValueKind.True));
    }

    [Test]
    public void ClientCapabilities_Meta_Should_Serialize_With_UnderscoreMeta()
    {
        var capabilities = new ClientCapabilities
        {
            Meta = new Dictionary<string, object?>
            {
                [ClientCapabilityMetadata.ExtensionsMetaKey] = new Dictionary<string, object?>
                {
                    [ClientCapabilityMetadata.AskUserExtensionMethod] = true
                }
            }
        };

        var json = JsonSerializer.Serialize(capabilities);
        var parsed = JsonDocument.Parse(json);

        Assert.That(parsed.RootElement.TryGetProperty("_meta", out var meta), Is.True);
        Assert.That(meta.TryGetProperty(ClientCapabilityMetadata.ExtensionsMetaKey, out var extensions), Is.True);
        Assert.That(
            extensions.GetProperty(ClientCapabilityMetadata.AskUserExtensionMethod).ValueKind,
            Is.EqualTo(JsonValueKind.True));
        Assert.That(
            extensions.TryGetProperty(ClientCapabilityMetadata.LegacyAskUserExtensionMethod, out _),
            Is.False);
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
        Assert.That(extensionMap.TryGetValue(ClientCapabilityMetadata.LegacyAskUserExtensionMethod, out _), Is.False);
    }

    [Test]
    public void ClientCapabilities_SupportsExtension_Should_Return_True_For_Default_AskUser_Metadata()
    {
        var capabilities = ClientCapabilityDefaults.Create();

        Assert.That(
            capabilities.SupportsExtension(ClientCapabilityMetadata.AskUserExtensionMethod),
            Is.True);
        Assert.That(
            capabilities.SupportsExtension(ClientCapabilityMetadata.LegacyAskUserExtensionMethod),
            Is.False);
    }

    [Test]
    public void ClientCapabilities_SupportsExtension_Should_Return_True_After_Json_RoundTrip()
    {
        var json = JsonSerializer.Serialize(ClientCapabilityDefaults.Create());
        var capabilities = JsonSerializer.Deserialize<ClientCapabilities>(json);

        Assert.That(capabilities, Is.Not.Null);
        Assert.That(
            capabilities!.SupportsExtension(ClientCapabilityMetadata.AskUserExtensionMethod),
            Is.True);
        Assert.That(
            capabilities.SupportsExtension(ClientCapabilityMetadata.LegacyAskUserExtensionMethod),
            Is.False);
    }

}
