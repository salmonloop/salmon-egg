using System;
using System.Linq;
using SalmonEgg.Acp.JsonRpc;
using Xunit;

namespace SalmonEgg.Acp.Tests.JsonRpc;

/// <summary>
/// The shared definition of "is this an inbound ACP frame", used by every transport. Keeping it in
/// one place is the point: a bridge that relays an agent's stdout over WebSocket delivers the same
/// non-ACP line a stdio pipe would, so both paths must reach the same answer.
/// </summary>
public sealed class AcpFrameTests
{
    [Theory]
    [InlineData(@"{""jsonrpc"":""2.0""}")]
    [InlineData(@"  {""jsonrpc"":""2.0""}")]
    [InlineData("﻿{\"jsonrpc\":\"2.0\"}")]
    public void LooksLikeFrame_JsonObject_ShouldBeTrue(string message)
    {
        Assert.True(AcpFrame.LooksLikeFrame(message));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("﻿")]
    [InlineData("﻿   ")]
    public void IsBlank_EmptyOrBomOnly_ShouldBeTrue(string? message)
    {
        // U+FEFF is not whitespace to string.IsNullOrWhiteSpace, so a BOM-only line would otherwise
        // be dispatched as a message and surface "'0xEF' is an invalid start of a value".
        Assert.True(AcpFrame.IsBlank(message));
        Assert.False(AcpFrame.LooksLikeFrame(message));
    }

    [Theory]
    // Causes that all reduce to the same parser message, plus the plain-text and ANSI cases
    // reported against other ACP clients.
    [InlineData("�{\"jsonrpc\":\"2.0\"}")]     // U+FFFD from a decode failure
    [InlineData(" loading...")]               // private-use glyph
    [InlineData("Running database migrations")]
    [InlineData("[1;32mINFO[0m ready")] // ANSI-coloured logging
    [InlineData("Invalid command line argument: -C")]
    [InlineData(@"[{""jsonrpc"":""2.0""}]")]        // batch: ACP messages are individual
    public void LooksLikeFrame_NonFrame_ShouldBeFalse(string message)
    {
        Assert.False(AcpFrame.IsBlank(message));
        Assert.False(AcpFrame.LooksLikeFrame(message));
    }

    [Fact]
    public void StripByteOrderMark_ShouldRemoveOnlyLeadingMarks()
    {
        // RFC 8259 §8.1 forbids emitting a byte order mark but explicitly permits parsers to
        // "ignore the presence of a byte order mark rather than treating it as an error".
        Assert.Equal(@"{""a"":1}", AcpFrame.StripByteOrderMark("﻿{\"a\":1}"));
        Assert.Equal(@"{""a"":1}", AcpFrame.StripByteOrderMark("﻿﻿{\"a\":1}"));

        // An interior mark is content, not framing, and must survive.
        Assert.Equal("{\"a\":\"﻿\"}", AcpFrame.StripByteOrderMark("{\"a\":\"﻿\"}"));
    }

    [Fact]
    public void Describe_ShouldExposeLeadingBytes()
    {
        // The distinct causes differ only in their leading bytes, so the hex prefix is what makes
        // them identifiable from logs alone.
        Assert.Contains("EF BB BF", AcpFrame.Describe("﻿"));
        Assert.Contains("hex: 52 75 6E", AcpFrame.Describe("Running database migrations"));
    }

    [Fact]
    public void Describe_Empty_ShouldNotRenderBlank()
    {
        Assert.Equal("<empty>", AcpFrame.Describe(""));
        Assert.Equal("<empty>", AcpFrame.Describe(null));
    }

    [Fact]
    public void Describe_LongMessage_ShouldTruncate()
    {
        var description = AcpFrame.Describe(new string('x', 500));

        Assert.Contains("…", description);
        Assert.True(description.Length < 300, $"Expected a bounded description; got {description.Length} chars.");
    }

    [Fact]
    public void Describe_TruncatingOnSurrogatePair_ShouldNotEmitLoneSurrogate()
    {
        // Truncating by UTF-16 index can split a surrogate pair. This renders malformed input, so a
        // lone surrogate here would be the very noise the hex prefix exists to resolve.
        const string astral = "\U0001F600"; // one code point, two UTF-16 code units
        var message = new string('x', 119) + astral + new string('y', 40);

        var description = AcpFrame.Describe(message);

        // A lone surrogate is not a scalar value, so rune enumeration substitutes U+FFFD for it;
        // an exact round-trip therefore proves none survived truncation.
        Assert.Equal(description, string.Concat(description.EnumerateRunes()));
    }
}
