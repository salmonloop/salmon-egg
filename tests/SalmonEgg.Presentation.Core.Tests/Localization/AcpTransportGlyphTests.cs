using SalmonEgg.Domain.Models;
using SalmonEgg.Presentation.Core.Localization;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Localization;

public sealed class AcpTransportGlyphTests
{
    [Theory]
    [InlineData(TransportType.Stdio, AcpTransportGlyph.StdioGlyph)]
    [InlineData(TransportType.WebSocket, AcpTransportGlyph.WebSocketGlyph)]
    [InlineData(TransportType.StreamableHttp, AcpTransportGlyph.StreamableHttpGlyph)]
    public void Resolve_MapsEachTransportToItsGlyph(TransportType transport, string expectedGlyph)
    {
        Assert.Equal(expectedGlyph, AcpTransportGlyph.Resolve(transport));
    }
}
