using System.Text.Json;
using SalmonEgg.Acp.Content;
using SalmonEgg.Acp.Mcp;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Acp.Serialization;

namespace SalmonEgg.Acp.Tests.Protocol;

public sealed class V2WireContractTests
{
    [Theory]
    [InlineData(AcpProtocolVersion.V1, "group", "id")]
    [InlineData(AcpProtocolVersion.V2, "groupId", "configId")]
    public void ConfigOptionGroup_NegotiatedVersion_UsesItsOwnIdentifierAtEveryRoot(int version, string groupId, string configId)
    {
        // Arrange
        var groupJson = $$"""{"{{groupId}}":"models","name":"Models","options":[{"value":"fast","name":"Fast"}]}""";
        var configJson = $$"""{"{{configId}}":"model","name":"Model","type":"select","currentValue":"fast","options":[{{groupJson}}]}""";

        // Act
        var group = Assert.IsType<ConfigOptionGroup>(JsonSerializer.Deserialize(groupJson, Wire.Of<ConfigOptionGroup>(version)));
        var config = Assert.IsType<ConfigOption>(JsonSerializer.Deserialize(configJson, Wire.Of<ConfigOption>(version)));
        using var direct = JsonDocument.Parse(JsonSerializer.Serialize(group, Wire.Of<ConfigOptionGroup>(version)));
        using var list = JsonDocument.Parse(JsonSerializer.Serialize(new List<ConfigOptionGroup> { group }, Wire.Of<List<ConfigOptionGroup>>(version)));
        using var parent = JsonDocument.Parse(JsonSerializer.Serialize(config, Wire.Of<ConfigOption>(version)));

        // Assert
        Assert.Equal("models", group.Group);
        Assert.Equal("models", Assert.Single(config.OptionGroups).Group);
        using var expected = JsonDocument.Parse(groupJson);
        Assert.True(JsonElement.DeepEquals(expected.RootElement, direct.RootElement));
        Assert.True(JsonElement.DeepEquals(expected.RootElement, list.RootElement[0]));
        Assert.True(JsonElement.DeepEquals(expected.RootElement, parent.RootElement.GetProperty("options")[0]));
    }

    [Theory]
    [InlineData("agent_message_chunk")]
    [InlineData("user_message_chunk")]
    [InlineData("agent_thought_chunk")]
    public void ContentChunkV2_MissingMessageId_RejectsEveryEnvelope(string discriminator)
    {
        // Arrange
        var json = $$$"""{"sessionUpdate":"{{{discriminator}}}","content":{"type":"text","text":"hello"}}""";

        // Act / Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(json, Wire.V2<SessionUpdate>()));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize($"[{json}]", Wire.V2<List<SessionUpdate>>()));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            $$"""{"sessionId":"s","update":{{json}}}""", Wire.V2<SessionUpdateParams>()));
    }

    [Theory]
    [InlineData("agent_message")]
    [InlineData("user_message")]
    [InlineData("agent_thought")]
    public void WholeMessageV2_MissingMessageId_RejectsEveryEnvelope(string discriminator)
    {
        // Arrange
        var json = $$"""{"sessionUpdate":"{{discriminator}}","content":[]}""";

        // Act / Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(json, Wire.V2<SessionUpdate>()));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize($"[{json}]", Wire.V2<List<SessionUpdate>>()));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            $$"""{"sessionId":"s","update":{{json}}}""", Wire.V2<SessionUpdateParams>()));
    }

    [Fact]
    public void WholeMessageV2_NullMessageId_RejectsReadAndWrite()
    {
        // Arrange
        var update = new AgentWholeMessageUpdate { MessageId = null! };

        // Act / Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize("""{"messageId":null}""", Wire.V2<AgentWholeMessageUpdate>()));
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(update, Wire.V2<AgentWholeMessageUpdate>()));
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(new SessionUpdateParams { SessionId = "s", Update = update }, Wire.V2<SessionUpdateParams>()));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("42")]
    public void ContentChunkV2_NonStringMessageId_RejectsDirectRoot(string rawValue)
    {
        // Arrange
        var json = $$$"""{"messageId":{{{rawValue}}},"content":{"type":"text","text":"hello"}}""";

        // Act / Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(json, Wire.V2<AgentMessageUpdate>()));
    }

    [Fact]
    public void ContentChunkV2_MissingMessageId_RejectsWritingEveryRoot()
    {
        // Arrange
        var chunk = new AgentMessageUpdate(new TextContentBlock("hello"));

        // Act / Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(chunk, Wire.V2<AgentMessageUpdate>()));
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize<SessionUpdate>(chunk, Wire.V2<SessionUpdate>()));
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(new List<SessionUpdate> { chunk }, Wire.V2<List<SessionUpdate>>()));
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(
            new SessionUpdateParams { SessionId = "s", Update = chunk }, Wire.V2<SessionUpdateParams>()));
    }

    [Theory]
    [InlineData("")]
    [InlineData(",\"messageId\":null")]
    [InlineData(",\"messageId\":42")]
    public void ContentChunkV1_OptionalOrInvalidMessageId_UsesSchemaDefault(string messageProperty)
    {
        // Arrange
        var json = $$$"""{"content":{"type":"text","text":"hello"}{{{messageProperty}}}}""";

        // Act
        var chunk = Assert.IsType<AgentMessageUpdate>(JsonSerializer.Deserialize(json, Wire.V1<AgentMessageUpdate>()));

        // Assert
        Assert.Null(chunk.MessageId);
        Assert.NotNull(chunk.Content);
    }

    [Theory]
    [InlineData(AcpProtocolVersion.V1)]
    [InlineData(AcpProtocolVersion.V2)]
    public void ResourceLink_Icons_PreservesRawMetadataAtEveryRoot(int version)
    {
        // Arrange
        const string json = """{"type":"resource_link","uri":"https://example.test/doc","name":"Doc","icons":[{"src":"https://example.test/icon.svg","theme":"future","vendor":{"size":1.20e+02}}]}""";

        // Act
        var direct = Assert.IsType<ResourceLinkContentBlock>(JsonSerializer.Deserialize(json, Wire.Of<ResourceLinkContentBlock>(version)));
        var parent = Assert.IsType<ResourceLinkContentBlock>(JsonSerializer.Deserialize(json, Wire.Of<ContentBlock>(version)));
        var list = JsonSerializer.Deserialize($"[{json}]", Wire.Of<List<ContentBlock>>(version));
        var replay = new[]
        {
            JsonSerializer.Serialize(direct, Wire.Of<ResourceLinkContentBlock>(version)),
            JsonSerializer.Serialize<ContentBlock>(parent, Wire.Of<ContentBlock>(version)),
            JsonSerializer.Serialize(Assert.Single(list!), Wire.Of<ContentBlock>(version))
        };

        // Assert
        using var expected = JsonDocument.Parse(json);
        foreach (var value in replay)
        {
            using var actual = JsonDocument.Parse(value);
            Assert.Equal(expected.RootElement.GetProperty("icons").GetRawText(), actual.RootElement.GetProperty("icons").GetRawText());
        }
    }

    [Theory]
    [InlineData(AcpProtocolVersion.V1)]
    [InlineData(AcpProtocolVersion.V2)]
    public void AvailableCommandInput_Text_UsesVersionedDiscriminatorAtEveryRoot(int version)
    {
        // Arrange
        var input = new AvailableCommandInput { Hint = "branch name" };
        var command = new AvailableCommand { Name = "review", Description = "Review", Input = input };

        // Act
        using var direct = JsonDocument.Parse(JsonSerializer.Serialize(input, Wire.Of<AvailableCommandInput>(version)));
        using var parent = JsonDocument.Parse(JsonSerializer.Serialize(command, Wire.Of<AvailableCommand>(version)));
        using var list = JsonDocument.Parse(JsonSerializer.Serialize(new List<AvailableCommand> { command }, Wire.Of<List<AvailableCommand>>(version)));

        // Assert
        Assert.Equal("branch name", direct.RootElement.GetProperty("hint").GetString());
        Assert.True(JsonElement.DeepEquals(direct.RootElement, parent.RootElement.GetProperty("input")));
        Assert.True(JsonElement.DeepEquals(direct.RootElement, list.RootElement[0].GetProperty("input")));
        Assert.Equal(version == AcpProtocolVersion.V2, direct.RootElement.TryGetProperty("type", out var type));
        if (version == AcpProtocolVersion.V2) Assert.Equal("text", type.GetString());
    }

    [Theory]
    [InlineData("_vendor_form")]
    [InlineData("future_form")]
    public void AvailableCommandInputV2_UnknownType_RoundTripsWithoutInventingText(string type)
    {
        // Arrange
        var json = $$$$"""{"name":"review","description":"Review","input":{"type":"{{{{type}}}}","schema":{"fields":[1.20e+02]}}}""";

        // Act
        var command = Assert.IsType<AvailableCommand>(JsonSerializer.Deserialize(json, Wire.V2<AvailableCommand>()));
        var replay = JsonSerializer.Serialize(command, Wire.V2<AvailableCommand>());

        // Assert
        using var expected = JsonDocument.Parse(json);
        using var actual = JsonDocument.Parse(replay);
        Assert.Equal(expected.RootElement.GetProperty("input").GetRawText(), actual.RootElement.GetProperty("input").GetRawText());
    }

    [Fact]
    public void TextCommandInput_DraftWire_UsesTextDiscriminator()
    {
        // Arrange
        var input = new TextCommandInput { Hint = "branch name" };

        // Act
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(input, Wire.V2<TextCommandInput>()));

        // Assert
        Assert.Equal("text", json.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public void McpServerV2_Sse_RemainsUnknownWithRawPayload()
    {
        // Arrange
        const string json = """{"type":"sse","name":"events","url":"https://example.test/events","future":{"encoding":1.20e+02}}""";

        // Act
        var server = JsonSerializer.Deserialize(json, Wire.V2<McpServer>());
        var setup = JsonSerializer.Deserialize($$"""{"cwd":"/tmp","mcpServers":[{{json}}]}""", Wire.V2<SessionNewParams>());

        // Assert
        Assert.IsType<CustomMcpServer>(server);
        Assert.IsType<CustomMcpServer>(Assert.Single(setup!.McpServers));
        Assert.Equal(json, JsonSerializer.Serialize(server, Wire.V2<McpServer>()));
    }

    [Theory]
    [InlineData("")]
    [InlineData(",\"type\":null")]
    [InlineData(",\"type\":42")]
    public void McpServerV2_MissingOrWrongDiscriminator_RejectsInsteadOfDefaultingToStdio(string typeProperty)
    {
        // Arrange
        var json = $$"""{"name":"mcp","command":"mcp"{{typeProperty}}}""";

        // Act / Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(json, Wire.V2<McpServer>()));
        var request = JsonSerializer.Deserialize(
            $$"""{"cwd":"/tmp","mcpServers":[{{json}}]}""", Wire.V2<SessionNewParams>());
        Assert.Empty(request!.McpServers);
    }

    [Fact]
    public void SessionResponsesV2_Modes_DoNotReadOrWriteV1State()
    {
        // Arrange
        const string json = """{"sessionId":"s","modes":{"currentModeId":"plan","availableModes":[]}}""";
        var modes = new SessionModesState { CurrentModeId = "plan" };

        // Act
        var created = JsonSerializer.Deserialize(json, Wire.V2<SessionNewResponse>());
        var resumed = JsonSerializer.Deserialize(json, Wire.V2<SessionResumeResponse>());
        using var createdJson = JsonDocument.Parse(JsonSerializer.Serialize(new SessionNewResponse("s", modes), Wire.V2<SessionNewResponse>()));
        using var resumedJson = JsonDocument.Parse(JsonSerializer.Serialize(new SessionResumeResponse(modes), Wire.V2<SessionResumeResponse>()));

        // Assert
        Assert.Null(created!.Modes);
        Assert.Null(resumed!.Modes);
        Assert.False(createdJson.RootElement.TryGetProperty("modes", out _));
        Assert.False(resumedJson.RootElement.TryGetProperty("modes", out _));
    }

    [Fact]
    public void AgentAuthCapabilitiesV2_LogoutMarker_DoesNotAdvertiseV1Capability()
    {
        // Arrange
        const string json = """{"logout":{}}""";
        var capabilities = new AgentAuthCapabilities { Logout = new LogoutCapabilities() };

        // Act
        var auth = JsonSerializer.Deserialize(json, Wire.V2<AgentAuthCapabilities>());
        var response = JsonSerializer.Deserialize(
            """{"protocolVersion":2,"info":{"name":"agent","version":"1"},"capabilities":{"auth":{"logout":{}}}}""",
            AcpJsonContext.Default.InitializeResponse);
        using var replay = JsonDocument.Parse(JsonSerializer.Serialize(capabilities, Wire.V2<AgentAuthCapabilities>()));

        // Assert
        Assert.Null(auth!.Logout);
        Assert.False(response!.AgentCapabilities.SupportsLogout);
        Assert.False(replay.RootElement.TryGetProperty("logout", out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData(",\"info\":null")]
    [InlineData(",\"info\":{}")]
    [InlineData(",\"info\":{\"name\":\"agent\"}")]
    [InlineData(",\"info\":{\"name\":false,\"version\":\"1\"}")]
    public void InitializeV2_MissingOrInvalidRequiredInfo_RejectsBothDirections(string infoProperty)
    {
        // Arrange
        var json = $$"""{"protocolVersion":2{{infoProperty}}}""";

        // Act / Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(json, AcpJsonContext.Default.InitializeParams));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(json, AcpJsonContext.Default.InitializeResponse));
    }

    [Theory]
    [InlineData("")]
    [InlineData(",\"capabilities\":null")]
    [InlineData(",\"capabilities\":false")]
    public void InitializeV2_DefaultableCapabilities_UsesEmptyCapabilities(string capabilitiesProperty)
    {
        // Arrange
        var json = $$$"""{"protocolVersion":2,"info":{"name":"peer","version":"1"}{{{capabilitiesProperty}}}}""";

        // Act
        var request = JsonSerializer.Deserialize(json, AcpJsonContext.Default.InitializeParams);
        var response = JsonSerializer.Deserialize(json, AcpJsonContext.Default.InitializeResponse);

        // Assert
        Assert.NotNull(request!.ClientCapabilities);
        Assert.False(response!.AgentCapabilities.SupportsLogout);
        Assert.False(response.AgentCapabilities.SupportsSessionList);
    }

    [Theory]
    [InlineData("")]
    [InlineData(",\"configOptions\":null")]
    [InlineData(",\"configOptions\":false")]
    public void SessionResponseV2_DefaultableConfigOptions_UsesAnEmptyList(string configProperty)
    {
        // Arrange
        var json = $$"""{"sessionId":"s"{{configProperty}}}""";

        // Act
        var created = JsonSerializer.Deserialize(json, Wire.V2<SessionNewResponse>());
        var resumed = JsonSerializer.Deserialize(json, Wire.V2<SessionResumeResponse>());

        // Assert
        Assert.Empty(Assert.IsType<List<ConfigOption>>(created!.ConfigOptions));
        Assert.Empty(Assert.IsType<List<ConfigOption>>(resumed!.ConfigOptions));
    }
}
