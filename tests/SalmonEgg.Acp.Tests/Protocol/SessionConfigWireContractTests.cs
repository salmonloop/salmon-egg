using System.Text.Json;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Acp.Serialization;

namespace SalmonEgg.Acp.Tests.Protocol;

public sealed class SessionConfigWireContractTests
{
    [Fact]
    public void SetOption_StringValue_UsesTheNegotiatedDiscriminator()
    {
        var request = new SessionSetConfigOptionParams("s", "model", "fast");

        var v1 = JsonSerializer.SerializeToElement(request, AcpJsonContext.Default.SessionSetConfigOptionParams);
        var v2 = JsonSerializer.SerializeToElement(request, Wire.V2<SessionSetConfigOptionParams>());

        Assert.False(v1.TryGetProperty("type", out _));
        Assert.Equal("id", v2.GetProperty("type").GetString());
        Assert.Equal("fast", v2.GetProperty("value").GetString());
    }

    [Theory]
    [InlineData("null")]
    [InlineData("false")]
    [InlineData("17")]
    [InlineData("\"text\"")]
    [InlineData("[1,2]")]
    [InlineData("{\"amount\":1e2,\"unit\":\"tokens\"}")]
    public void SetOption_UnknownV2Type_PreservesRawValueAndExtensions(string value)
    {
        var json = "{\"sessionId\":\"s\",\"configId\":\"budget\",\"type\":\"_budget\",\"value\":"
            + value + ",\"future\":{\"nested\":true},\"_meta\":{\"trace\":\"t\"}}";

        var request = JsonSerializer.Deserialize(json, Wire.V2<SessionSetConfigOptionParams>());
        Assert.NotNull(request);
        var result = JsonSerializer.SerializeToElement(request, Wire.V2<SessionSetConfigOptionParams>());

        Assert.Equal("_budget", result.GetProperty("type").GetString());
        Assert.Equal(value, result.GetProperty("value").GetRawText());
        Assert.True(result.GetProperty("future").GetProperty("nested").GetBoolean());
        Assert.Equal("t", result.GetProperty("_meta").GetProperty("trace").GetString());
    }

    [Theory]
    [InlineData("{\"value\":\"fast\"}")]
    [InlineData("{\"type\":null,\"value\":\"fast\"}")]
    [InlineData("{\"type\":false,\"value\":\"fast\"}")]
    [InlineData("{\"type\":\"id\",\"value\":true}")]
    [InlineData("{\"type\":\"boolean\",\"value\":\"true\"}")]
    [InlineData("{\"type\":\"_future\"}")]
    public void SetOption_V2RequiredDiscriminatorAndKnownValueTypes_RemainStrict(string fields)
    {
        var json = "{\"sessionId\":\"s\",\"configId\":\"c\"," + fields[1..];

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(json, Wire.V2<SessionSetConfigOptionParams>()));
    }

    [Fact]
    public void SetOption_V2KnownType_PreservesUnknownFields()
    {
        const string json = """{"sessionId":"s","configId":"mode","type":"id","value":"plan","future":1e2}""";

        var request = JsonSerializer.Deserialize(json, Wire.V2<SessionSetConfigOptionParams>());
        Assert.NotNull(request);
        var result = JsonSerializer.SerializeToElement(request, Wire.V2<SessionSetConfigOptionParams>());

        Assert.Equal("id", result.GetProperty("type").GetString());
        Assert.Equal("1e2", result.GetProperty("future").GetRawText());
    }

    [Theory]
    [InlineData("{\"configId\":\"b\",\"name\":\"Enabled\",\"type\":\"boolean\",\"currentValue\":true,\"_meta\":false}")]
    [InlineData("{\"configId\":\"s\",\"name\":\"Model\",\"type\":\"select\",\"currentValue\":\"fast\",\"options\":[{\"value\":\"fast\",\"name\":\"Fast\",\"_meta\":false}]}")]
    [InlineData("{\"configId\":\"s\",\"name\":\"Model\",\"type\":\"select\",\"currentValue\":\"fast\",\"options\":[{\"groupId\":\"g\",\"name\":\"Group\",\"_meta\":false,\"options\":[{\"value\":\"fast\",\"name\":\"Fast\"}]}]}")]
    public void ConfigOption_V2InvalidOptionalMetadata_DoesNotDropValidConfiguration(string json)
    {
        var result = JsonSerializer.Deserialize(json, Wire.V2<ConfigOption>());

        Assert.NotNull(result);
        Assert.Null(result.Meta);
        if (result.Type == "select")
        {
            var value = result.Options.Count > 0 ? Assert.Single(result.Options) : Assert.Single(Assert.Single(result.OptionGroups).Options);
            Assert.Equal("fast", value.Value);
            Assert.Null(value.Meta);
        }
    }
}
