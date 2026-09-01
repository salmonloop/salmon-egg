using System.Text.Json;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Acp.Serialization;
using Xunit;

namespace SalmonEgg.Acp.Tests.Protocol;

public sealed class ConfigOptionV2Tests
{
    [Fact]
    public void ConfigOption_ReadsV2ConfigId()
    {
        var option = JsonSerializer.Deserialize<ConfigOption>(
            "{\"configId\":\"mode\",\"name\":\"Mode\",\"type\":\"boolean\",\"currentValue\":true}",
            AcpJsonContext.Default.ConfigOption);
        Assert.Equal("mode", option!.Id);
    }

    [Fact]
    public void ConfigOption_WritesVersionSpecificIdentifier()
    {
        var option = new ConfigOption { Id = "mode", Name = "Mode", Type = "boolean", CurrentBooleanValue = true };
        var v1 = JsonSerializer.Serialize(option, AcpJsonContext.Default.ConfigOption);
        string v2;
        using (AcpProtocolWriteContext.Enter(AcpProtocolVersion.V2))
            v2 = JsonSerializer.Serialize(option, AcpJsonContext.Default.ConfigOption);
        Assert.Contains("\"id\":\"mode\"", v1, StringComparison.Ordinal);
        Assert.DoesNotContain("\"configId\"", v1, StringComparison.Ordinal);
        Assert.Contains("\"configId\":\"mode\"", v2, StringComparison.Ordinal);
        Assert.DoesNotContain("\"id\":\"mode\"", v2, StringComparison.Ordinal);
    }
}
