using System;
using System.Globalization;
using SalmonEgg.Presentation.Core.Utilities;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Utilities;

public sealed class AcpSessionTimestampPolicyTests
{
    [Fact]
    public void ParseUpdatedAtUtc_WhenNull_ReturnsNull()
    {
        Assert.Null(AcpSessionTimestampPolicy.ParseUpdatedAtUtc(null));
    }

    [Fact]
    public void ParseUpdatedAtUtc_WhenBlank_ReturnsNull()
    {
        Assert.Null(AcpSessionTimestampPolicy.ParseUpdatedAtUtc("   "));
    }

    [Fact]
    public void ParseUpdatedAtUtc_WhenInvalid_ReturnsNull()
    {
        Assert.Null(AcpSessionTimestampPolicy.ParseUpdatedAtUtc("not-a-timestamp"));
    }

    [Fact]
    public void ParseUpdatedAtUtc_WhenZuluNormalizesToUtc()
    {
        var parsed = AcpSessionTimestampPolicy.ParseUpdatedAtUtc("2026-07-20T10:30:00Z");

        Assert.NotNull(parsed);
        Assert.Equal(DateTimeKind.Utc, parsed!.Value.Kind);
        Assert.Equal(2026, parsed.Value.Year);
        Assert.Equal(7, parsed.Value.Month);
        Assert.Equal(20, parsed.Value.Day);
        Assert.Equal(10, parsed.Value.Hour);
        Assert.Equal(30, parsed.Value.Minute);
    }

    [Fact]
    public void ParseUpdatedAtUtc_WhenOffsetNormalizesToUtc()
    {
        // +02:00 offset must collapse to the equivalent UTC instant so remote and local
        // sessions compare on the same timeline across platforms.
        var parsed = AcpSessionTimestampPolicy.ParseUpdatedAtUtc("2026-07-20T12:30:00+02:00");

        Assert.NotNull(parsed);
        Assert.Equal(DateTimeKind.Utc, parsed!.Value.Kind);
        Assert.Equal(10, parsed.Value.Hour);
    }

    [Fact]
    public void ParseUpdatedAtUtc_WhenNoOffsetAssumesUniversal()
    {
        var parsed = AcpSessionTimestampPolicy.ParseUpdatedAtUtc("2026-07-20T10:30:00");

        Assert.NotNull(parsed);
        Assert.Equal(DateTimeKind.Utc, parsed!.Value.Kind);
        Assert.Equal(10, parsed.Value.Hour);
    }

    [Fact]
    public void ResolveLatestUpdatedAtUtc_WhenExistingMissing_ReturnsIncoming()
    {
        var incoming = new DateTime(2026, 7, 20, 10, 0, 0, DateTimeKind.Utc);

        Assert.Equal(incoming, AcpSessionTimestampPolicy.ResolveLatestUpdatedAtUtc(null, incoming));
    }

    [Fact]
    public void ResolveLatestUpdatedAtUtc_WhenIncomingDefault_ReturnsExisting()
    {
        var existing = new DateTime(2026, 7, 20, 10, 0, 0, DateTimeKind.Utc);

        Assert.Equal(existing, AcpSessionTimestampPolicy.ResolveLatestUpdatedAtUtc(existing, default));
    }

    [Fact]
    public void ResolveLatestUpdatedAtUtc_WhenBothMissing_ReturnsNull()
    {
        Assert.Null(AcpSessionTimestampPolicy.ResolveLatestUpdatedAtUtc(null, null));
    }

    [Fact]
    public void ResolveLatestUpdatedAtUtc_WhenIncomingNewer_ReturnsIncoming()
    {
        var existing = new DateTime(2026, 7, 20, 10, 0, 0, DateTimeKind.Utc);
        var incoming = existing.AddHours(1);

        Assert.Equal(incoming, AcpSessionTimestampPolicy.ResolveLatestUpdatedAtUtc(existing, incoming));
    }

    [Fact]
    public void ResolveLatestUpdatedAtUtc_WhenExistingNewer_ReturnsExisting()
    {
        var existing = new DateTime(2026, 7, 20, 11, 0, 0, DateTimeKind.Utc);
        var incoming = existing.AddHours(-1);

        Assert.Equal(existing, AcpSessionTimestampPolicy.ResolveLatestUpdatedAtUtc(existing, incoming));
    }
}
