using System.Text.Json;
using SalmonEgg.Domain.Models.Content;
using SalmonEgg.Domain.Models.Protocol;
using SalmonEgg.Domain.Models.Session;
using SalmonEgg.Infrastructure.Serialization;
using Xunit;

namespace SalmonEgg.Infrastructure.Tests.Serialization;

public sealed class AcpJsonContextTests
{
    [Fact]
    public void AuthenticateResponse_SerializesWithGeneratedContextAsEmptyObject()
    {
        var json = JsonSerializer.Serialize(
            new AuthenticateResponse(),
            AcpJsonContext.Default.AuthenticateResponse);

        Assert.Equal("{}", json);
    }

    [Fact]
    public void InitializeDtos_SerializeWireShape_WithGeneratedContext()
    {
        var initializeParamsJson = JsonSerializer.Serialize(
            new InitializeParams
            {
                ProtocolVersion = 1,
                Meta = new Dictionary<string, object?>
                {
                    ["foo"] = "bar",
                    ["count"] = 3,
                    ["nullValue"] = null
                }
            },
            AcpJsonContext.Default.InitializeParams);
        var initializeResponseJson = JsonSerializer.Serialize(
            new InitializeResponse
            {
                ProtocolVersion = 1
            },
            AcpJsonContext.Default.InitializeResponse);

        using var initializeParams = JsonDocument.Parse(initializeParamsJson);
        using var initializeResponse = JsonDocument.Parse(initializeResponseJson);
        var initializeMeta = initializeParams.RootElement.GetProperty("_meta");

        Assert.Equal(JsonValueKind.Number, initializeParams.RootElement.GetProperty("protocolVersion").ValueKind);
        Assert.Equal(JsonValueKind.Number, initializeResponse.RootElement.GetProperty("protocolVersion").ValueKind);
        Assert.DoesNotContain("\"2024-11-05\"", initializeParamsJson, StringComparison.Ordinal);
        Assert.DoesNotContain("\"2024-11-05\"", initializeResponseJson, StringComparison.Ordinal);
        Assert.Equal(JsonValueKind.String, initializeMeta.GetProperty("foo").ValueKind);
        Assert.Equal(JsonValueKind.Number, initializeMeta.GetProperty("count").ValueKind);
        Assert.Equal(JsonValueKind.Null, initializeMeta.GetProperty("nullValue").ValueKind);
    }

    [Fact]
    public void CapabilityDtos_RoundTripWireShape_WithGeneratedContext()
    {
        var agentCapabilitiesJson = JsonSerializer.Serialize(
            new AgentCapabilities
            {
                SessionCapabilities = new SessionCapabilities
                {
                    List = new SessionListCapabilities()
                }
            },
            AcpJsonContext.Default.AgentCapabilities);
        var clientCapabilitiesJson = JsonSerializer.Serialize(
            ClientCapabilityDefaults.Create(),
            AcpJsonContext.Default.ClientCapabilities);
        var mcpCapabilitiesJson = JsonSerializer.Serialize(
            new McpCapabilities(
                http: true,
                sse: false,
                meta: new Dictionary<string, object?>
                {
                    ["vendor"] = "agent",
                    ["priority"] = 2
                }),
            AcpJsonContext.Default.McpCapabilities);

        using var agentDocument = JsonDocument.Parse(agentCapabilitiesJson);
        using var clientDocument = JsonDocument.Parse(clientCapabilitiesJson);
        using var mcpDocument = JsonDocument.Parse(mcpCapabilitiesJson);

        Assert.True(agentDocument.RootElement.TryGetProperty("sessionCapabilities", out var sessionCaps));
        Assert.True(sessionCaps.TryGetProperty("list", out _));
        Assert.True(clientDocument.RootElement.TryGetProperty("_meta", out var clientMeta));
        Assert.True(clientMeta.TryGetProperty(ClientCapabilityMetadata.ExtensionsMetaKey, out var extensions));
        Assert.Equal(JsonValueKind.True, extensions.GetProperty(ClientCapabilityMetadata.AskUserExtensionMethod).ValueKind);
        Assert.False(extensions.TryGetProperty("interaction.ask_user", out _));
        Assert.Equal(JsonValueKind.True, mcpDocument.RootElement.GetProperty("http").ValueKind);
        Assert.Equal(JsonValueKind.False, mcpDocument.RootElement.GetProperty("sse").ValueKind);
        Assert.Equal("agent", mcpDocument.RootElement.GetProperty("_meta").GetProperty("vendor").GetString());

        var agentCapabilities = JsonSerializer.Deserialize(
            """
            {
              "loadSession": true,
              "sessionCapabilities": {
                "resume": {},
                "close": {},
                "list": {}
              }
            }
            """,
            AcpJsonContext.Default.AgentCapabilities);
        var initializeResponse = JsonSerializer.Deserialize(
            """
            {
              "protocolVersion": 1,
              "agentInfo": { "name": "agent", "version": "1.0.0" },
              "agentCapabilities": {},
              "_meta": { "source": "unit-test", "flag": true }
            }
            """,
            AcpJsonContext.Default.InitializeResponse);
        var mcpCapabilities = JsonSerializer.Deserialize(
            """
            {
              "http": true,
              "sse": true,
              "_meta": { "vendor": "agent" }
            }
            """,
            AcpJsonContext.Default.McpCapabilities);
        var clientCapabilities = JsonSerializer.Deserialize(
            clientCapabilitiesJson,
            AcpJsonContext.Default.ClientCapabilities);

        Assert.NotNull(agentCapabilities);
        Assert.True(agentCapabilities!.SupportsSessionLoading);
        Assert.True(agentCapabilities.SupportsSessionResume);
        Assert.True(agentCapabilities.SupportsSessionClose);
        Assert.True(agentCapabilities.SupportsSessionList);
        Assert.NotNull(initializeResponse!.Meta);
        Assert.Equal("unit-test", ((JsonElement)initializeResponse.Meta!["source"]!).GetString());
        Assert.Equal(JsonValueKind.True, ((JsonElement)initializeResponse.Meta["flag"]!).ValueKind);
        Assert.True(mcpCapabilities!.Http);
        Assert.True(mcpCapabilities.Sse);
        Assert.Equal("agent", ((JsonElement)mcpCapabilities.Meta!["vendor"]!).GetString());
        Assert.True(clientCapabilities!.SupportsExtension(ClientCapabilityMetadata.AskUserExtensionMethod));
        Assert.False(clientCapabilities.SupportsExtension("interaction.ask_user"));
    }

    [Fact]
    public void ReviewedStandardProtocolDtos_RoundTrip_WithGeneratedContext()
    {
        var promptResponseJson = JsonSerializer.Serialize(
            new SessionPromptResponse(StopReason.EndTurn),
            AcpJsonContext.Default.SessionPromptResponse);
        var promptResponse = JsonSerializer.Deserialize(
            promptResponseJson,
            AcpJsonContext.Default.SessionPromptResponse);

        var setModeResponseJson = JsonSerializer.Serialize(
            new SessionSetModeResponse(),
            AcpJsonContext.Default.SessionSetModeResponse);
        var setModeResponse = JsonSerializer.Deserialize(
            setModeResponseJson,
            AcpJsonContext.Default.SessionSetModeResponse);

        var capabilitiesJson = JsonSerializer.Serialize(
            ClientCapabilityDefaults.Create(),
            AcpJsonContext.Default.ClientCapabilities);
        var capabilities = JsonSerializer.Deserialize(
            capabilitiesJson,
            AcpJsonContext.Default.ClientCapabilities);
        var agentCapabilitiesJson = JsonSerializer.Serialize(
            new AgentCapabilities(
                sessionCapabilities: new SessionCapabilities
                {
                    AdditionalDirectories = new SessionAdditionalDirectoriesCapabilities(),
                    Delete = new SessionDeleteCapabilities()
                },
                auth: new AgentAuthCapabilities
                {
                    Logout = new LogoutCapabilities()
                }),
            AcpJsonContext.Default.AgentCapabilities);
        var agentCapabilities = JsonSerializer.Deserialize(
            agentCapabilitiesJson,
            AcpJsonContext.Default.AgentCapabilities);
        var currentModeJson = JsonSerializer.Serialize(
            new SessionUpdateParams("session-1", new CurrentModeUpdate("code")),
            AcpJsonContext.Default.SessionUpdateParams);
        var currentMode = JsonSerializer.Deserialize(
            currentModeJson,
            AcpJsonContext.Default.SessionUpdateParams);

        Assert.Equal(StopReason.EndTurn, promptResponse!.StopReason);
        Assert.NotNull(setModeResponse);
        Assert.NotNull(capabilities);
        Assert.True(capabilities!.SupportsExtension(ClientCapabilityMetadata.AskUserExtensionMethod));
        Assert.NotNull(capabilities.Session?.ConfigOptions);
        Assert.True(agentCapabilities!.SupportsSessionAdditionalDirectories);
        Assert.True(agentCapabilities.SupportsSessionDelete);
        Assert.True(agentCapabilities.SupportsLogout);
        Assert.IsType<CurrentModeUpdate>(currentMode!.Update);
        Assert.Equal("code", ((CurrentModeUpdate)currentMode.Update).ModeId);
    }

    [Fact]
    public void ReviewedStandardProtocolDtos_DoNotSerializeNonStandardRootFields()
    {
        var promptParams = new SessionPromptParams(
            "session-1",
            new List<ContentBlock>
            {
                new TextContentBlock { Text = "hi" }
            });

        var promptJson = JsonSerializer.Serialize(
            promptParams,
            AcpJsonContext.Default.SessionPromptParams);
        var promptResponseJson = JsonSerializer.Serialize(
            new SessionPromptResponse(StopReason.EndTurn),
            AcpJsonContext.Default.SessionPromptResponse);
        var setModeResponseJson = JsonSerializer.Serialize(
            new SessionSetModeResponse(),
            AcpJsonContext.Default.SessionSetModeResponse);
        var deleteResponseJson = JsonSerializer.Serialize(
            new SessionDeleteResponse(),
            AcpJsonContext.Default.SessionDeleteResponse);
        var logoutResponseJson = JsonSerializer.Serialize(
            new LogoutResponse(),
            AcpJsonContext.Default.LogoutResponse);

        using var promptDocument = JsonDocument.Parse(promptJson);
        using var promptResponseDocument = JsonDocument.Parse(promptResponseJson);
        using var setModeResponseDocument = JsonDocument.Parse(setModeResponseJson);
        using var currentModeDocument = JsonDocument.Parse(JsonSerializer.Serialize(
            new SessionUpdateParams("session-1", new CurrentModeUpdate("code")),
            AcpJsonContext.Default.SessionUpdateParams));

        Assert.False(promptDocument.RootElement.TryGetProperty("maxTokens", out _));
        Assert.False(promptDocument.RootElement.TryGetProperty("stopSequences", out _));
        Assert.False(promptDocument.RootElement.TryGetProperty("messageId", out _));
        Assert.False(promptResponseDocument.RootElement.TryGetProperty("userMessageId", out _));
        Assert.False(setModeResponseDocument.RootElement.TryGetProperty("modeId", out _));
        Assert.Equal("{}", deleteResponseJson);
        Assert.Equal("{}", logoutResponseJson);
        var update = currentModeDocument.RootElement.GetProperty("update");
        Assert.Equal("code", update.GetProperty("currentModeId").GetString());
        Assert.False(update.TryGetProperty("modeId", out _));
    }
}
