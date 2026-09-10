using System.Text.Json;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Acp.Serialization;

namespace SalmonEgg.Acp.Tests.Protocol;

public sealed class ElicitationMultiSelectItemsTests
{
    [Theory]
    [InlineData(AcpProtocolVersion.V1, false)]
    [InlineData(AcpProtocolVersion.V1, true)]
    [InlineData(AcpProtocolVersion.V2, false)]
    [InlineData(AcpProtocolVersion.V2, true)]
    public void Read_StringEnumWithOptionalLegacyType_RoundTripsSchemaWithoutInventedType(int version, bool includeLegacyType)
    {
        // Arrange: StringMultiSelectItems is identified by its required enum, not a type property.
        var json = "{" + (includeLegacyType ? "\"type\":\"string\"," : string.Empty)
            + "\"enum\":[\"api-internal\",\"ui-internal\"],\"_meta\":{\"source\":\"agent\"}}";
        var typeInfo = AcpWireFormat.For(version).TypeInfo<MultiSelectItems>();

        // Act
        var parsed = JsonSerializer.Deserialize(json, typeInfo);
        Assert.NotNull(parsed);
        var written = JsonSerializer.Serialize<MultiSelectItems>(parsed, typeInfo);

        // Assert
        Assert.Equal(["api-internal", "ui-internal"], Assert.IsType<StringMultiSelectItems>(parsed).Enum);
        using var document = JsonDocument.Parse(written);
        Assert.False(document.RootElement.TryGetProperty("type", out _));
        Assert.Equal("agent", document.RootElement.GetProperty("_meta").GetProperty("source").GetString());
        Assert.Equal(["api-internal", "ui-internal"],
            Assert.IsType<StringMultiSelectItems>(JsonSerializer.Deserialize(written, typeInfo)).Enum);
    }

    [Theory]
    [InlineData("{\"enum\":null}")]
    [InlineData("{\"enum\":\"api\"}")]
    [InlineData("{\"enum\":[\"api\",7]}")]
    [InlineData("{\"type\":\"string\",\"enum\":[null]}")]
    [InlineData("{\"type\":7,\"enum\":[\"api\"]}")]
    [InlineData("{\"anyOf\":null}")]
    [InlineData("{\"anyOf\":\"api\"}")]
    [InlineData("{\"anyOf\":[\"api\"]}")]
    [InlineData("{\"anyOf\":[{\"const\":7,\"title\":\"API\"}]}")]
    public void Read_KnownItemsWithWrongFieldTypes_Throws(string json)
    {
        // Act / Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(json, AcpJsonContext.Default.MultiSelectItems));
    }

    [Theory]
    [InlineData("_vendorTree")]
    [InlineData("futureTree")]
    public void Read_UnknownTypeWithEnum_KeepsRawPayloadInsteadOfRenderingKnownOptions(string type)
    {
        // Arrange
        var json = "{\"type\":\"" + type + "\",\"enum\":[\"api\"],\"scale\":1.2300e+02}";

        // Act
        var parsed = JsonSerializer.Deserialize(json, AcpJsonContext.Default.MultiSelectItems);

        // Assert
        Assert.Equal(type, Assert.IsType<CustomMultiSelectItems>(parsed).ItemsType);
        Assert.Equal(json, JsonSerializer.Serialize(parsed, AcpJsonContext.Default.MultiSelectItems));
    }
}
