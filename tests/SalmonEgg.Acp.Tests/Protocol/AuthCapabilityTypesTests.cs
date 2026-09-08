using System.Text.Json;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Acp.Serialization;

namespace SalmonEgg.Acp.Tests.Protocol;

public sealed class AuthCapabilityTypesTests
{
    [Theory]
    [InlineData(AcpProtocolVersion.V1, "", false)]
    [InlineData(AcpProtocolVersion.V1, "\"terminal\":null", false)]
    [InlineData(AcpProtocolVersion.V1, "\"terminal\":true", true)]
    [InlineData(AcpProtocolVersion.V1, "\"terminal\":false", false)]
    [InlineData(AcpProtocolVersion.V1, "\"terminal\":{}", false)]
    [InlineData(AcpProtocolVersion.V1, "\"terminal\":42", false)]
    [InlineData(AcpProtocolVersion.V2, "", false)]
    [InlineData(AcpProtocolVersion.V2, "\"terminal\":null", false)]
    [InlineData(AcpProtocolVersion.V2, "\"terminal\":true", false)]
    [InlineData(AcpProtocolVersion.V2, "\"terminal\":false", false)]
    [InlineData(AcpProtocolVersion.V2, "\"terminal\":{}", true)]
    [InlineData(AcpProtocolVersion.V2, "\"terminal\":42", false)]
    public void AuthCapabilities_Terminal_UsesOnlyTheNegotiatedWireShape(int version, string property, bool expected)
    {
        // Arrange
        var json = $"{{{property}}}";

        // Act
        var direct = Assert.IsType<AuthCapabilities>(JsonSerializer.Deserialize(json, Wire.Of<AuthCapabilities>(version)));
        var parent = Assert.IsType<ClientCapabilities>(JsonSerializer.Deserialize($$"""{"auth":{{json}}}""", Wire.Of<ClientCapabilities>(version)));

        // Assert
        Assert.Equal(expected, direct.Terminal);
        Assert.Equal(expected, Assert.IsType<AuthCapabilities>(parent.Auth).Terminal);
    }

    [Theory]
    [InlineData(AcpProtocolVersion.V1, "false")]
    [InlineData(AcpProtocolVersion.V1, "42")]
    [InlineData(AcpProtocolVersion.V1, "[]")]
    [InlineData(AcpProtocolVersion.V2, "false")]
    [InlineData(AcpProtocolVersion.V2, "42")]
    [InlineData(AcpProtocolVersion.V2, "[]")]
    public void AuthCapabilities_InvalidType_IsStrictAtRootAndDefaultableAsCapability(int version, string invalid)
    {
        // Arrange / Act / Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(invalid, Wire.Of<AuthCapabilities>(version)));
        var parent = Assert.IsType<ClientCapabilities>(JsonSerializer.Deserialize($$"""{"auth":{{invalid}}}""", Wire.Of<ClientCapabilities>(version)));
        Assert.Null(parent.Auth);
    }

    [Theory]
    [InlineData(AcpProtocolVersion.V1, true, "{\"terminal\":true}")]
    [InlineData(AcpProtocolVersion.V1, false, "{\"terminal\":false}")]
    [InlineData(AcpProtocolVersion.V2, true, "{\"terminal\":{}}")]
    [InlineData(AcpProtocolVersion.V2, false, "{}")]
    public void AuthCapabilities_Writing_UsesOnlyTheNegotiatedWireShape(int version, bool terminal, string expected)
    {
        // Arrange
        var capabilities = new AuthCapabilities { Terminal = terminal };

        // Act
        var json = JsonSerializer.Serialize(capabilities, Wire.Of<AuthCapabilities>(version));

        // Assert
        Assert.Equal(expected, json);
    }

    [Fact]
    public void AuthCapabilities_DefaultSourceGeneratedContract_RemainsStable()
    {
        // Arrange
        var capabilities = new AuthCapabilities { Terminal = true };

        // Act
        var json = JsonSerializer.Serialize(capabilities, AcpJsonContext.Default.AuthCapabilities);
        var restored = JsonSerializer.Deserialize(json, AcpJsonContext.Default.AuthCapabilities);

        // Assert
        Assert.Equal("{\"terminal\":true}", json);
        Assert.True(Assert.IsType<AuthCapabilities>(restored).Terminal);
        Assert.Null(ClientCapabilityDefaults.Create().Auth);
    }

    [Theory]
    [InlineData(AcpProtocolVersion.V1)]
    [InlineData(AcpProtocolVersion.V2)]
    public void AuthCapabilities_UnknownFields_SurviveKnownValueChanges(int version)
    {
        // Arrange
        const string future = """{"number":1.20e+02,"text":"\u4f60"}""";
        var json = $$"""{"future":{{future}},"_meta":false}""";
        var parsed = Assert.IsType<AuthCapabilities>(JsonSerializer.Deserialize(json, Wire.Of<AuthCapabilities>(version)));

        // Act
        using var replay = JsonDocument.Parse(JsonSerializer.Serialize(parsed with { Terminal = true }, Wire.Of<AuthCapabilities>(version)));

        // Assert
        Assert.Null(parsed.Meta);
        Assert.Equal(future, replay.RootElement.GetProperty("future").GetRawText());
        Assert.Equal(version == AcpProtocolVersion.V1 ? JsonValueKind.True : JsonValueKind.Object,
            replay.RootElement.GetProperty("terminal").ValueKind);
    }
}
