using System.Text.Json;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Acp.Serialization;

namespace SalmonEgg.Acp.Tests.Protocol;

public sealed class V2WireReviewBoundaryTests
{
    [Theory]
    [InlineData(AcpProtocolVersion.V1, "direct")]
    [InlineData(AcpProtocolVersion.V1, "list")]
    [InlineData(AcpProtocolVersion.V1, "nested")]
    [InlineData(AcpProtocolVersion.V2, "direct")]
    [InlineData(AcpProtocolVersion.V2, "list")]
    [InlineData(AcpProtocolVersion.V2, "nested")]
    public void ConfigGroup_UnknownFields_SurviveWithoutOverwritingIdentity(int version, string root)
    {
        // Arrange
        var groupId = version == AcpProtocolVersion.V1 ? "group" : "groupId";
        var unknownId = version == AcpProtocolVersion.V1 ? "groupId" : "group";
        var configId = version == AcpProtocolVersion.V1 ? "id" : "configId";
        const string future = """{"number":1.20e+02,"text":"\u4f60"}""";
        var json = $$"""{"{{groupId}}":"models","{{unknownId}}":"unrelated","name":"Models","future":{{future}},"options":[{"value":"fast","name":"Fast","future":{{future}}}]}""";

        // Act
        var replay = root switch
        {
            "direct" => RoundTrip(json, Wire.Of<ConfigOptionGroup>(version)),
            "list" => RoundTrip($"[{json}]", Wire.Of<List<ConfigOptionGroup>>(version)),
            _ => RoundTrip($$"""{"{{configId}}":"model","name":"Model","type":"select","currentValue":"fast","options":[{{json}}]}""", Wire.Of<ConfigOption>(version))
        };
        using var document = JsonDocument.Parse(replay);
        var group = root == "direct" ? document.RootElement
            : root == "list" ? document.RootElement[0] : document.RootElement.GetProperty("options")[0];

        // Assert
        Assert.Equal("models", group.GetProperty(groupId).GetString());
        Assert.Equal("unrelated", group.GetProperty(unknownId).GetString());
        Assert.Equal(future, group.GetProperty("future").GetRawText());
        Assert.Equal(future, group.GetProperty("options")[0].GetProperty("future").GetRawText());
    }

    [Theory]
    [InlineData(AcpProtocolVersion.V1, "direct")]
    [InlineData(AcpProtocolVersion.V1, "list")]
    [InlineData(AcpProtocolVersion.V1, "nested")]
    [InlineData(AcpProtocolVersion.V2, "direct")]
    [InlineData(AcpProtocolVersion.V2, "list")]
    [InlineData(AcpProtocolVersion.V2, "nested")]
    public void ConfigValue_UnknownFields_SurviveAtEveryRoot(int version, string root)
    {
        // Arrange
        var configId = version == AcpProtocolVersion.V1 ? "id" : "configId";
        const string future = """{"number":1.20e+02,"text":"\u4f60"}""";
        var json = $$"""{"value":"fast","name":"Fast","future":{{future}}}""";

        // Act
        var replay = root switch
        {
            "direct" => RoundTrip(json, Wire.Of<ConfigOptionValue>(version)),
            "list" => RoundTrip($"[{json}]", Wire.Of<List<ConfigOptionValue>>(version)),
            _ => RoundTrip($$"""{"{{configId}}":"model","name":"Model","type":"select","currentValue":"fast","options":[{{json}}]}""", Wire.Of<ConfigOption>(version))
        };
        using var document = JsonDocument.Parse(replay);
        var option = root == "direct" ? document.RootElement
            : root == "list" ? document.RootElement[0] : document.RootElement.GetProperty("options")[0];

        // Assert
        Assert.Equal("fast", option.GetProperty("value").GetString());
        Assert.Equal(future, option.GetProperty("future").GetRawText());
    }

    [Theory]
    [InlineData(AcpProtocolVersion.V1, true)]
    [InlineData(AcpProtocolVersion.V1, false)]
    [InlineData(AcpProtocolVersion.V2, true)]
    [InlineData(AcpProtocolVersion.V2, false)]
    public void ClientAuthCapability_ValidTerminal_SurvivesDirectAndInitialize(int version, bool direct)
    {
        // Arrange
        var terminal = version == AcpProtocolVersion.V1 ? "true" : """{"_meta":{"source":"agent"},"future":1.20e+02}""";
        var capabilities = $$$$"""{"auth":{"terminal":{{{{terminal}}}}},"elicitation":{"form":{}}}""";
        var json = version == AcpProtocolVersion.V1
            ? $$"""{"protocolVersion":1,"clientInfo":{"name":"client","version":"1"},"clientCapabilities":{{capabilities}}}"""
            : $$"""{"protocolVersion":2,"info":{"name":"client","version":"1"},"capabilities":{{capabilities}}}""";

        // Act
        var replay = direct ? RoundTrip(capabilities, Wire.Of<ClientCapabilities>(version))
            : RoundTrip(json, AcpJsonContext.Default.InitializeParams);
        using var document = JsonDocument.Parse(replay);
        var result = direct ? document.RootElement
            : document.RootElement.GetProperty(version == AcpProtocolVersion.V1 ? "clientCapabilities" : "capabilities");

        // Assert
        Assert.Equal(terminal, result.GetProperty("auth").GetProperty("terminal").GetRawText());
        Assert.Equal(JsonValueKind.Object, result.GetProperty("elicitation").GetProperty("form").ValueKind);
    }

    [Theory]
    [InlineData("false", true)]
    [InlineData("42", true)]
    [InlineData("[]", true)]
    [InlineData("false", false)]
    [InlineData("42", false)]
    [InlineData("[]", false)]
    public void ClientCapabilitiesV2_InvalidElicitation_PreservesValidAuth(string invalid, bool direct)
    {
        // Arrange
        var capabilities = $$$$"""{"elicitation":{{{{invalid}}}},"auth":{"terminal":{}},"_meta":{"peer":"client"}}""";

        // Act
        var replay = RoundTripClientCapabilities(capabilities, direct);
        using var document = JsonDocument.Parse(replay);
        var result = direct ? document.RootElement : document.RootElement.GetProperty("capabilities");

        // Assert
        Assert.False(result.TryGetProperty("elicitation", out _));
        Assert.Equal(JsonValueKind.Object, result.GetProperty("auth").GetProperty("terminal").ValueKind);
        Assert.Equal("client", result.GetProperty("_meta").GetProperty("peer").GetString());
    }

    [Theory]
    [InlineData("false", true)]
    [InlineData("42", true)]
    [InlineData("[]", true)]
    [InlineData("false", false)]
    [InlineData("42", false)]
    [InlineData("[]", false)]
    public void ClientCapabilitiesV2_InvalidAuth_PreservesValidElicitation(string invalid, bool direct)
    {
        // Arrange
        var capabilities = $$$$"""{"auth":{{{{invalid}}}},"elicitation":{"form":{}},"_meta":{"peer":"client"}}""";

        // Act
        var replay = RoundTripClientCapabilities(capabilities, direct);
        using var document = JsonDocument.Parse(replay);
        var result = direct ? document.RootElement : document.RootElement.GetProperty("capabilities");

        // Assert
        Assert.False(result.TryGetProperty("auth", out _));
        Assert.Equal(JsonValueKind.Object, result.GetProperty("elicitation").GetProperty("form").ValueKind);
        Assert.Equal("client", result.GetProperty("_meta").GetProperty("peer").GetString());
    }

    [Theory]
    [InlineData("form", "url", true)]
    [InlineData("url", "form", true)]
    [InlineData("form", "url", false)]
    [InlineData("url", "form", false)]
    public void ElicitationV2_InvalidMarker_PreservesValidSibling(string invalid, string valid, bool direct)
    {
        // Arrange
        var elicitation = $$$$"""{"{{{{invalid}}}}":false,"{{{{valid}}}}":{},"_meta":{"peer":"client"}}""";

        // Act
        var replay = direct ? RoundTrip(elicitation, Wire.V2<ElicitationCapabilities>())
            : RoundTripClientCapabilities($$"""{"elicitation":{{elicitation}}}""", false);
        using var document = JsonDocument.Parse(replay);
        var result = direct ? document.RootElement : document.RootElement.GetProperty("capabilities").GetProperty("elicitation");

        // Assert
        Assert.False(result.TryGetProperty(invalid, out _));
        Assert.Equal(JsonValueKind.Object, result.GetProperty(valid).ValueKind);
        Assert.Equal("client", result.GetProperty("_meta").GetProperty("peer").GetString());
    }

    [Theory]
    [InlineData("false", true)]
    [InlineData("42", true)]
    [InlineData("[]", true)]
    [InlineData("false", false)]
    [InlineData("42", false)]
    [InlineData("[]", false)]
    public void AgentCapabilitiesV2_InvalidAuth_PreservesValidSibling(string invalid, bool direct)
    {
        // Arrange
        var capabilities = $$$$"""{"auth":{{{{invalid}}}},"session":{"prompt":{"image":{}}},"_meta":{"peer":"agent"}}""";

        // Act
        var replay = direct ? RoundTrip(capabilities, Wire.V2<AgentCapabilities>())
            : RoundTrip($$"""{"protocolVersion":2,"info":{"name":"agent","version":"1"},"capabilities":{{capabilities}}}""", AcpJsonContext.Default.InitializeResponse);
        using var document = JsonDocument.Parse(replay);
        var result = direct ? document.RootElement : document.RootElement.GetProperty("capabilities");

        // Assert
        Assert.False(result.TryGetProperty("auth", out _));
        Assert.Equal("agent", result.GetProperty("_meta").GetProperty("peer").GetString());
        if (!direct)
        {
            Assert.Equal(JsonValueKind.Object, result.GetProperty("session").GetProperty("prompt").GetProperty("image").ValueKind);
        }
    }

    [Theory]
    [InlineData("false")]
    [InlineData("42")]
    [InlineData("[]")]
    public void CapabilitiesV2_InvalidTypedRoot_RemainsStrict(string invalid)
    {
        // Arrange / Act / Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(invalid, Wire.V2<ElicitationCapabilities>()));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(invalid, Wire.V2<AgentAuthCapabilities>()));
    }

    private static string RoundTripClientCapabilities(string capabilities, bool direct)
        => direct ? RoundTrip(capabilities, Wire.V2<ClientCapabilities>())
            : RoundTrip($$"""{"protocolVersion":2,"info":{"name":"client","version":"1"},"capabilities":{{capabilities}}}""", AcpJsonContext.Default.InitializeParams);

    private static string RoundTrip<T>(string json, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
        => JsonSerializer.Serialize(JsonSerializer.Deserialize(json, typeInfo), typeInfo);
}
