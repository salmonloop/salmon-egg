using System.Reflection;
using System.Text;
using SalmonEgg.Infrastructure.Transport;
using Xunit;

namespace SalmonEgg.Infrastructure.Tests.Transport;

public sealed class StdioTransportEncodingTests
{
    [Fact]
    public void Constructor_DefaultEncoding_ShouldUseUtf8WithoutBom()
    {
        using var transport = new StdioTransport("agent-command");

        var encoding = GetEncoding(transport);

        Assert.Equal(Encoding.UTF8.WebName, encoding.WebName);
        Assert.Empty(encoding.GetPreamble());
    }

    [Fact]
    public void Constructor_WhenGivenUtf8WithBom_ShouldNormalizeToUtf8WithoutBom()
    {
        using var transport = new StdioTransport("agent-command", encoding: Encoding.UTF8);

        var encoding = GetEncoding(transport);

        Assert.Equal(Encoding.UTF8.WebName, encoding.WebName);
        Assert.Empty(encoding.GetPreamble());
    }

    private static Encoding GetEncoding(StdioTransport transport)
    {
        var field = typeof(StdioTransport).GetField("_encoding", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);

        var value = field!.GetValue(transport);
        var encoding = Assert.IsAssignableFrom<Encoding>(value);
        return encoding;
    }
}
