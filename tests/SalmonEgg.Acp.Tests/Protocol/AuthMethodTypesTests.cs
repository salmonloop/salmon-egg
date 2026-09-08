using System.Text.Json;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Acp.Serialization;

namespace SalmonEgg.Acp.Tests.Protocol;

public sealed class AuthMethodTypesTests
{
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("Agent")]
    [InlineData("AGENT")]
    [InlineData("terminal")]
    [InlineData("_vendor_x")]
    [InlineData("future_thing")]
    public void Deserialize_UnsupportedStringDiscriminator_RoundTripsWithoutMakingItExecutable(string methodType)
    {
        // Arrange
        var json = $$"""{"id":"login","name":"Login","type":"{{JsonEncodedText.Encode(methodType)}}"}""";

        // Act
        var method = JsonSerializer.Deserialize(json, AcpJsonContext.Default.AuthMethodDefinition);
        var replay = JsonSerializer.Serialize(method, AcpJsonContext.Default.AuthMethodDefinition);

        // Assert
        Assert.NotNull(method);
        Assert.False(method.SupportsAuthenticateRequest);
        Assert.Equal(methodType, method.ResolvedType);
        using var replayDocument = JsonDocument.Parse(replay);
        Assert.Equal(methodType, replayDocument.RootElement.GetProperty("type").GetString());
    }

    [Theory]
    [InlineData("null")]
    [InlineData("42")]
    [InlineData("false")]
    [InlineData("[]")]
    [InlineData("{}")]
    public void Deserialize_NonStringDiscriminator_ThrowsJsonException(string rawType)
    {
        // Arrange
        var json = $$"""{"id":"login","name":"Login","type":{{rawType}}}""";

        // Act / Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(json, AcpJsonContext.Default.AuthMethodDefinition));
    }

    [Theory]
    [InlineData("terminal")]
    [InlineData("_vendor_x")]
    [InlineData("future_thing")]
    public void Serialize_UnsupportedMethod_PreservesPayloadAndTypedEdits(string methodType)
    {
        // Arrange
        var json = $$$"""
            {
              "id": "login", "name": "Login", "type": "{{{methodType}}}",
              "args": ["login", "--interactive"], "env": {"AUTH_MODE": "interactive"},
              "futurePayload": {"items": [true, null, 1.2300e+02], "display": "\u4f60\u597d"},
              "_meta": {"vendor": {"hint": "preserve"}}
            }
            """;
        var method = Assert.IsType<AuthMethodDefinition>(
            JsonSerializer.Deserialize(json, AcpJsonContext.Default.AuthMethodDefinition));

        // Act
        var replay = JsonSerializer.Serialize(method with { Description = "Updated hint" }, AcpJsonContext.Default.AuthMethodDefinition);

        // Assert
        using var expected = JsonDocument.Parse(json);
        using var actual = JsonDocument.Parse(replay);
        Assert.Equal("Updated hint", actual.RootElement.GetProperty("description").GetString());
        foreach (var property in expected.RootElement.EnumerateObject())
        {
            Assert.True(JsonElement.DeepEquals(property.Value, actual.RootElement.GetProperty(property.Name)));
        }

        Assert.Equal(
            expected.RootElement.GetProperty("futurePayload").GetRawText(),
            actual.RootElement.GetProperty("futurePayload").GetRawText());
    }

    [Theory]
    [InlineData(AcpProtocolVersion.V1, "id")]
    [InlineData(AcpProtocolVersion.V2, "methodId")]
    public void SerializeInitializeResponse_UnsupportedMethods_PreservesDiscriminatorsAndPayload(int version, string idProperty)
    {
        // Arrange
        var methodJson = $$$"""
            {"{{{idProperty}}}":"login","name":"Login","type":" ","args":["login"],"env":{"AUTH_MODE":"interactive"},"future":{"key":1}}
            """;
        var initializeJson = $$"""
            {"protocolVersion":{{version}},"agentCapabilities":{},"capabilities":{},"authMethods":[{{methodJson}}]}
            """;
        var response = Assert.IsType<InitializeResponse>(
            JsonSerializer.Deserialize(initializeJson, AcpJsonContext.Default.InitializeResponse));

        // Act
        var replay = JsonSerializer.Serialize(response, AcpJsonContext.Default.InitializeResponse);

        // Assert
        using var expected = JsonDocument.Parse(methodJson);
        using var actual = JsonDocument.Parse(replay);
        var method = actual.RootElement.GetProperty("authMethods")[0];
        Assert.True(JsonElement.DeepEquals(expected.RootElement, method));
    }

    [Theory]
    [InlineData(AcpProtocolVersion.V1, "id", null)]
    [InlineData(AcpProtocolVersion.V1, "id", "agent")]
    [InlineData(AcpProtocolVersion.V1, "id", " ")]
    [InlineData(AcpProtocolVersion.V1, "id", "future_thing")]
    [InlineData(AcpProtocolVersion.V2, "methodId", null)]
    [InlineData(AcpProtocolVersion.V2, "methodId", "agent")]
    [InlineData(AcpProtocolVersion.V2, "methodId", " ")]
    [InlineData(AcpProtocolVersion.V2, "methodId", "future_thing")]
    public void Serialize_NegotiatedVersion_UsesSameAuthShapeAtEveryRoot(int version, string idProperty, string? type)
    {
        // Arrange
        var method = new AuthMethodDefinition { Id = "login", Name = "Login", Type = type };
        var response = new InitializeResponse { ProtocolVersion = version, AuthMethods = [method] };

        // Act
        using var direct = JsonDocument.Parse(JsonSerializer.Serialize(method, Wire.Of<AuthMethodDefinition>(version)));
        using var list = JsonDocument.Parse(JsonSerializer.Serialize(new List<AuthMethodDefinition> { method }, Wire.Of<List<AuthMethodDefinition>>(version)));
        using var initialize = JsonDocument.Parse(JsonSerializer.Serialize(response, AcpJsonContext.Default.InitializeResponse));

        // Assert
        var expected = initialize.RootElement.GetProperty("authMethods")[0];
        Assert.Equal("login", expected.GetProperty(idProperty).GetString());
        Assert.True(JsonElement.DeepEquals(expected, direct.RootElement));
        Assert.True(JsonElement.DeepEquals(expected, list.RootElement[0]));
        if (version == AcpProtocolVersion.V1 && type is null)
        {
            Assert.False(expected.TryGetProperty("type", out _));
        }
        else
        {
            Assert.Equal(type ?? AuthMethodDefinition.AgentType, expected.GetProperty("type").GetString());
        }
    }

    [Theory]
    [InlineData("method")]
    [InlineData("list")]
    [InlineData("initialize")]
    public void DeserializeV2_AbsentRequiredDiscriminator_RejectsAtEveryRoot(string root)
    {
        // Arrange
        const string method = """{"methodId":"login","name":"Login"}""";

        // Act / Assert
        Assert.Throws<JsonException>(() =>
        {
            switch (root)
            {
                case "method":
                    JsonSerializer.Deserialize(method, Wire.V2<AuthMethodDefinition>());
                    break;
                case "list":
                    JsonSerializer.Deserialize($"[{method}]", Wire.V2<List<AuthMethodDefinition>>());
                    break;
                default:
                    JsonSerializer.Deserialize(
                        $$"""{"protocolVersion":2,"capabilities":{},"authMethods":[{{method}}]}""",
                        AcpJsonContext.Default.InitializeResponse);
                    break;
            }
        });
    }

    [Theory]
    [InlineData(AcpProtocolVersion.V1, "id", "methodId")]
    [InlineData(AcpProtocolVersion.V2, "methodId", "id")]
    public void Deserialize_UnknownIdentityField_PreservesItWithoutChangingTheMethodId(int version, string idProperty, string unknownIdProperty)
    {
        // Arrange
        var json = $$"""
            {"{{idProperty}}":"login","{{unknownIdProperty}}":"other","name":"Login","type":"future_thing"}
            """;

        // Act
        var method = Assert.IsType<AuthMethodDefinition>(JsonSerializer.Deserialize(json, Wire.Of<AuthMethodDefinition>(version)));
        var replay = JsonSerializer.Serialize(method, Wire.Of<AuthMethodDefinition>(version));

        // Assert
        Assert.Equal("login", method.Id);
        using var expected = JsonDocument.Parse(json);
        using var actual = JsonDocument.Parse(replay);
        Assert.True(JsonElement.DeepEquals(expected.RootElement, actual.RootElement));
    }

    [Fact]
    public void Serialize_ArbitraryDiscriminator_PreservesEligibilityAndWireValue()
        => FsCheckPropertyRunner.Run(this, nameof(DiscriminatorRoundTripProperty));

    private void DiscriminatorRoundTripProperty(string? type)
    {
        // Arrange
        var method = new AuthMethodDefinition { Id = "login", Name = "Login", Type = type };

        // Act
        var json = JsonSerializer.Serialize(method, AcpJsonContext.Default.AuthMethodDefinition);
        var replay = Assert.IsType<AuthMethodDefinition>(
            JsonSerializer.Deserialize(json, AcpJsonContext.Default.AuthMethodDefinition));

        // Assert
        Assert.Equal(type, replay.Type);
        Assert.Equal(type is null || type == AuthMethodDefinition.AgentType, replay.SupportsAuthenticateRequest);
    }
}
