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
