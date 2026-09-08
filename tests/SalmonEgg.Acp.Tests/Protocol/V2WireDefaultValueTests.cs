using System.Text.Json;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Acp.Serialization;

namespace SalmonEgg.Acp.Tests.Protocol;

public sealed class V2WireDefaultValueTests
{
    [Theory]
    [InlineData("null")]
    [InlineData("false")]
    [InlineData("42")]
    [InlineData("{}")]
    public void ConfigOptionsV2_InvalidArrayValue_DefaultsAtEveryParent(string value)
    {
        // Arrange
        var json = $$"""{"sessionId":"session","configOptions":{{value}}}""";

        // Act
        var created = Assert.IsType<SessionNewResponse>(JsonSerializer.Deserialize(json, Wire.V2<SessionNewResponse>()));
        var resumed = Assert.IsType<SessionResumeResponse>(JsonSerializer.Deserialize(json, Wire.V2<SessionResumeResponse>()));
        var configured = Assert.IsType<SessionSetConfigOptionResponse>(JsonSerializer.Deserialize(json, Wire.V2<SessionSetConfigOptionResponse>()));
        var update = Assert.IsType<ConfigOptionUpdate>(JsonSerializer.Deserialize(json, Wire.V2<ConfigOptionUpdate>()));

        // Assert
        Assert.Empty(Assert.IsType<List<ConfigOption>>(created.ConfigOptions));
        Assert.Empty(Assert.IsType<List<ConfigOption>>(resumed.ConfigOptions));
        Assert.Empty(Assert.IsType<List<ConfigOption>>(configured.ConfigOptions));
        Assert.Empty(Assert.IsType<List<ConfigOption>>(update.ConfigOptions));
    }

    [Fact]
    public void ConfigOptionsV2_MissingRequiredArray_RejectsUpdateAndSetResponse()
    {
        // Arrange / Act / Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize("{}", Wire.V2<SessionSetConfigOptionResponse>()));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize("{}", Wire.V2<ConfigOptionUpdate>()));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            """{"sessionId":"session","update":{"sessionUpdate":"config_option_update"}}""", Wire.V2<SessionUpdateParams>()));
    }

    [Fact]
    public void ConfigOptionsV2_MissingAuthoredState_RejectsWritingARequiredArray()
    {
        // Arrange
        var update = new ConfigOptionUpdate();

        // Act / Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(new SessionSetConfigOptionResponse(), Wire.V2<SessionSetConfigOptionResponse>()));
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(update, Wire.V2<ConfigOptionUpdate>()));
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(new SessionUpdateParams { SessionId = "session", Update = update }, Wire.V2<SessionUpdateParams>()));
    }

    [Fact]
    public void InitializeV2_MissingAuthoredInfo_RejectsBothDirections()
    {
        // Arrange
        var request = new InitializeParams { ProtocolVersion = AcpProtocolVersion.V2, ClientInfo = null! };
        var response = new InitializeResponse { ProtocolVersion = AcpProtocolVersion.V2, AgentInfo = null! };

        // Act / Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(request, AcpJsonContext.Default.InitializeParams));
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(response, AcpJsonContext.Default.InitializeResponse));
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(request with { ClientInfo = new ClientInfo { Name = null! } }, Wire.V2<InitializeParams>()));
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(response with { AgentInfo = new AgentInfo { Version = null! } }, Wire.V2<InitializeResponse>()));
    }

    [Fact]
    public void ContentChunkV1_DefaultSourceGeneratedContract_UsesTheSameSchemaDefault()
    {
        // Arrange
        const string json = """{"content":{"type":"text","text":"hello"},"messageId":false}""";

        // Act
        var chunk = Assert.IsType<AgentMessageUpdate>(JsonSerializer.Deserialize(json, AcpJsonContext.Default.AgentMessageUpdate));

        // Assert
        Assert.Null(chunk.MessageId);
    }

    [Fact]
    public void ConfigOptionsV2_InvalidItems_KeepValidAndUnknownSuccessors()
    {
        // Arrange
        const string unknown = """{"configId":"future","name":"Future","type":"_future","payload":{"size":1.20e+02}}""";
        var json = $$$"""
            {"sessionId":"session","configOptions":[
              {},null,false,{"configId":"broken","name":"Broken","type":"boolean","currentValue":"wrong"},
              {"configId":"enabled","name":"Enabled","type":"boolean","currentValue":true},{{{unknown}}}
            ]}
            """;

        // Act
        var created = Assert.IsType<SessionNewResponse>(JsonSerializer.Deserialize(json, Wire.V2<SessionNewResponse>()));
        var resumed = Assert.IsType<SessionResumeResponse>(JsonSerializer.Deserialize(json, Wire.V2<SessionResumeResponse>()));
        var configured = Assert.IsType<SessionSetConfigOptionResponse>(JsonSerializer.Deserialize(json, Wire.V2<SessionSetConfigOptionResponse>()));
        var update = Assert.IsType<ConfigOptionUpdate>(JsonSerializer.Deserialize(json, Wire.V2<ConfigOptionUpdate>()));

        // Assert
        foreach (var options in new[] { created.ConfigOptions, resumed.ConfigOptions, configured.ConfigOptions, update.ConfigOptions })
        {
            var items = Assert.IsType<List<ConfigOption>>(options);
            Assert.Equal(new[] { "enabled", "future" }, items.Select(option => option.Id));
            Assert.Equal(unknown, JsonSerializer.Serialize(items[1], Wire.V2<ConfigOption>()));
        }

        using var replay = JsonDocument.Parse(JsonSerializer.Serialize(created, Wire.V2<SessionNewResponse>()));
        Assert.Equal(unknown, replay.RootElement.GetProperty("configOptions")[1].GetRawText());
    }

    [Theory]
    [InlineData(AcpProtocolVersion.V1, "group")]
    [InlineData(AcpProtocolVersion.V2, "groupId")]
    public void ConfigGroup_DefaultableOptions_KeepTheValidSuccessor(int version, string groupId)
    {
        // Arrange
        var json = $$"""{"{{groupId}}":"models","name":"Models","options":[false,{},{"value":"fast","name":"Fast","description":42}]}""";

        // Act
        var group = Assert.IsType<ConfigOptionGroup>(JsonSerializer.Deserialize(json, Wire.Of<ConfigOptionGroup>(version)));

        // Assert
        var option = Assert.Single(group.Options);
        Assert.Equal("fast", option.Value);
        Assert.Null(option.Description);
    }

    [Theory]
    [InlineData(AcpProtocolVersion.V1, "group")]
    [InlineData(AcpProtocolVersion.V2, "groupId")]
    public void ConfigGroup_MissingAndInvalidOptions_RespectRequiredAndDefaultableContracts(int version, string groupId)
    {
        // Arrange
        var missing = $$"""{"{{groupId}}":"models","name":"Models"}""";
        var invalid = $$"""{"{{groupId}}":"models","name":"Models","options":false}""";

        // Act / Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(missing, Wire.Of<ConfigOptionGroup>(version)));
        var group = Assert.IsType<ConfigOptionGroup>(JsonSerializer.Deserialize(invalid, Wire.Of<ConfigOptionGroup>(version)));
        Assert.Empty(group.Options);
    }

    [Theory]
    [InlineData(AcpProtocolVersion.V1, "id", "configId")]
    [InlineData(AcpProtocolVersion.V2, "configId", "id")]
    public void ConfigOption_ConflictingVersionIdentifiers_PreservesUnknownFieldWithoutChangingIdentity(int version, string id, string unknownId)
    {
        // Arrange
        var json = $$"""{"{{id}}":"current","{{unknownId}}":"other","name":"Current","type":"boolean","currentValue":true}""";

        // Act
        var option = Assert.IsType<ConfigOption>(JsonSerializer.Deserialize(json, Wire.Of<ConfigOption>(version)));
        using var replay = JsonDocument.Parse(JsonSerializer.Serialize(option, Wire.Of<ConfigOption>(version)));

        // Assert
        Assert.Equal("current", option.Id);
        Assert.Equal("other", replay.RootElement.GetProperty(unknownId).GetString());
    }

    [Theory]
    [InlineData(AcpProtocolVersion.V1, "configId")]
    [InlineData(AcpProtocolVersion.V2, "id")]
    public void ConfigOption_MissingVersionIdentifier_RejectsTheOtherVersionAlias(int version, string otherId)
    {
        // Arrange
        var json = $$"""{"{{otherId}}":"current","name":"Current","type":"boolean","currentValue":true}""";

        // Act / Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(json, Wire.Of<ConfigOption>(version)));
    }

    [Theory]
    [InlineData("")]
    [InlineData(",\"configOptions\":null")]
    public void SessionResponsesV1_OptionalConfigOptions_RemainsUnprovided(string property)
    {
        // Arrange
        var json = $$"""{"sessionId":"session"{{property}}}""";

        // Act
        var created = Assert.IsType<SessionNewResponse>(JsonSerializer.Deserialize(json, Wire.V1<SessionNewResponse>()));
        var resumed = Assert.IsType<SessionResumeResponse>(JsonSerializer.Deserialize(json, Wire.V1<SessionResumeResponse>()));

        // Assert
        Assert.Null(created.ConfigOptions);
        Assert.Null(resumed.ConfigOptions);
    }

    [Fact]
    public void ClientCapabilitiesV2_LegacyFields_AreIgnoredOnReadAndRejectedOnWrite()
    {
        // Arrange
        const string capabilities = """{"fs":{"readTextFile":true},"terminal":true,"session":{"configOptions":{}}}""";
        var json = $$"""{"protocolVersion":2,"info":{"name":"client","version":"1"},"capabilities":{{capabilities}}}""";
        var legacy = new ClientCapabilities(fs: new FsCapability(), terminal: true);

        // Act
        var direct = Assert.IsType<ClientCapabilities>(JsonSerializer.Deserialize(capabilities, Wire.V2<ClientCapabilities>()));
        var parent = Assert.IsType<InitializeParams>(JsonSerializer.Deserialize(json, AcpJsonContext.Default.InitializeParams));

        // Assert
        Assert.Null(direct.Fs);
        Assert.Null(direct.Terminal);
        Assert.Null(direct.Session);
        Assert.Equal(direct, parent.ClientCapabilities);
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(legacy, Wire.V2<ClientCapabilities>()));
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(new InitializeParams { ProtocolVersion = 2, ClientCapabilities = legacy }, AcpJsonContext.Default.InitializeParams));
    }

    [Theory]
    [InlineData("{\"name\":\"peer\",\"version\":null}")]
    [InlineData("[]")]
    [InlineData("false")]
    public void InitializeV2_InvalidInfo_RejectsBothRoots(string info)
    {
        // Arrange
        var json = $$"""{"protocolVersion":2,"info":{{info}}}""";

        // Act / Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(json, AcpJsonContext.Default.InitializeParams));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(json, AcpJsonContext.Default.InitializeResponse));
    }
}
