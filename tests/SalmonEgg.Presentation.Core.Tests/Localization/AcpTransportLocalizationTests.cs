using SalmonEgg.Domain.Models;
using SalmonEgg.Presentation.Core.Localization;

namespace SalmonEgg.Presentation.Core.Tests.Localization;

public sealed class AcpTransportLocalizationTests
{
    [Theory]
    [InlineData(TransportType.Stdio, AcpTransportLocalization.StdioResourceKey, "Stdio (subprocess)")]
    [InlineData(TransportType.WebSocket, AcpTransportLocalization.WebSocketResourceKey, "WebSocket")]
    [InlineData(TransportType.HttpSse, AcpTransportLocalization.HttpSseResourceKey, "HTTP SSE")]
    public void ResolveResourceKeyAndFallback_MatchCanonicalLabels(
        TransportType transport,
        string resourceKey,
        string fallback)
    {
        Assert.Equal(resourceKey, AcpTransportLocalization.ResolveResourceKey(transport));
        Assert.Equal(fallback, AcpTransportLocalization.ResolveInvariantFallback(transport));
    }

    [Fact]
    public void TryResolveTransport_FromServerConfiguration_UsesTransportProperty()
    {
        var configuration = new ServerConfiguration { Transport = TransportType.HttpSse };

        var resolved = AcpTransportLocalization.TryResolveTransport(configuration, out var transport);

        Assert.True(resolved);
        Assert.Equal(TransportType.HttpSse, transport);
    }

    [Fact]
    public void TryResolveTransport_FromUnknownValue_ReturnsFalse()
    {
        var resolved = AcpTransportLocalization.TryResolveTransport(42, out var transport);

        Assert.False(resolved);
        Assert.Equal(default, transport);
    }
}
