using SalmonEgg.Infrastructure.Transport;
using Xunit;

namespace SalmonEgg.Infrastructure.Tests.Transport;

/// <summary>
/// ACP reserves stdout for newline-delimited ACP messages and directs agent diagnostics to stderr,
/// so a line that never looked like a frame is misrouted logging rather than a protocol error.
/// These pin the classification that keeps such lines out of the JSON-RPC layer.
/// </summary>
public sealed class StdioTransportStdoutFrameTests
{
    [Theory]
    [InlineData("{\"jsonrpc\":\"2.0\",\"method\":\"session/update\"}")]
    [InlineData("  {\"jsonrpc\":\"2.0\"}")]
    public void ClassifyStdoutLine_AcpFrame_ShouldDispatch(string line)
    {
        var kind = StdioTransport.ClassifyStdoutLine(line, out var frame);

        Assert.Equal(StdioTransport.StdoutFrameKind.Frame, kind);
        Assert.Equal(line, frame);
    }

    [Fact]
    public void ClassifyStdoutLine_BomPrefixedFrame_ShouldStripBomAndDispatch()
    {
        // RFC 8259 §8.1 forbids emitting a byte order mark but explicitly permits parsers to
        // "ignore the presence of a byte order mark rather than treating it as an error".
        const string payload = "{\"jsonrpc\":\"2.0\"}";

        var kind = StdioTransport.ClassifyStdoutLine("﻿" + payload, out var frame);

        Assert.Equal(StdioTransport.StdoutFrameKind.Frame, kind);
        Assert.Equal(payload, frame);
        Assert.DoesNotContain('﻿', frame);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("﻿")]
    [InlineData("﻿   ")]
    public void ClassifyStdoutLine_BlankOrBomOnly_ShouldBeIgnored(string line)
    {
        // A lone byte order mark is a blank line. U+FEFF is not whitespace to
        // string.IsNullOrWhiteSpace, so without the strip it would reach the parser and surface a
        // protocol error ("'0xEF' is an invalid start of a value") for an empty line.
        var kind = StdioTransport.ClassifyStdoutLine(line, out var frame);

        Assert.Equal(StdioTransport.StdoutFrameKind.Blank, kind);
        Assert.Empty(frame);
    }

    [Theory]
    // Observed causes that all reduce to the same '0xEF' parser message, plus the plain-text and
    // ANSI cases reported against other ACP clients.
    [InlineData("�{\"jsonrpc\":\"2.0\"}")]         // U+FFFD from a decode failure
    [InlineData(" loading...")]                   // private-use glyph (Nerd Font)
    [InlineData("Running database migrations")]         // agent startup logging
    [InlineData("[1;32mINFO[0m ready")]     // ANSI-coloured logging
    [InlineData("Invalid command line argument: -C")]
    [InlineData("[{\"jsonrpc\":\"2.0\"}]")]             // batch: ACP messages are individual
    public void ClassifyStdoutLine_NonFrame_ShouldBeDiagnostic(string line)
    {
        var kind = StdioTransport.ClassifyStdoutLine(line, out var frame);

        Assert.Equal(StdioTransport.StdoutFrameKind.Diagnostic, kind);
        Assert.Empty(frame);
    }
}
