using System;
using SalmonEgg.Presentation.Core.Services.Chat;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Services.Chat;

public sealed class ConversationMessageTimestampTests
{
    [Fact]
    public void InstantEquals_TreatsUnspecifiedUtcTicksAsSameInstantAsUtc()
    {
        // Persistence / some ACP paths may surface unspecified Kind with UTC ticks.
        // Display conversion and patch matching must still treat them as one instant.
        var utc = new DateTime(2026, 7, 18, 12, 0, 0, DateTimeKind.Utc);
        var unspecified = DateTime.SpecifyKind(utc, DateTimeKind.Unspecified);

        Assert.True(ConversationMessageTimestamp.InstantEquals(utc, unspecified));
        Assert.Equal(
            ConversationMessageTimestamp.ToDisplayLocal(utc),
            ConversationMessageTimestamp.ToDisplayLocal(unspecified));
    }

    [Fact]
    public void InstantEquals_LocalAndUtcRepresentSameInstant()
    {
        var utc = new DateTime(2026, 7, 18, 12, 0, 0, DateTimeKind.Utc);
        var local = utc.ToLocalTime();

        Assert.True(ConversationMessageTimestamp.InstantEquals(utc, local));
        Assert.True(ConversationMessageTimestamp.InstantEquals(
            ConversationMessageTimestamp.ToDisplayLocal(utc),
            local));
    }

    [Fact]
    public void ToAuthoritativeUtc_NormalizesLocalToUtc()
    {
        var local = new DateTime(2026, 7, 18, 8, 0, 0, DateTimeKind.Local);
        var utc = ConversationMessageTimestamp.ToAuthoritativeUtc(local);

        Assert.NotNull(utc);
        Assert.Equal(DateTimeKind.Utc, utc!.Value.Kind);
        Assert.Equal(local.ToUniversalTime(), utc.Value);
    }

    [Fact]
    public void InstantEquals_NullAndValue_AreNotEqual()
    {
        Assert.False(ConversationMessageTimestamp.InstantEquals(
            null,
            new DateTime(2026, 7, 18, 12, 0, 0, DateTimeKind.Utc)));
        Assert.True(ConversationMessageTimestamp.InstantEquals(null, null));
    }
}
