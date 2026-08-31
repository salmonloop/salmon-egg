using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Presentation.Core.Services.Chat;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Services.Chat;

public sealed class TranscriptItemKeyTests
{
    [Fact]
    public void PrefersMessageId()
    {
        var key = TranscriptItemKey.FromSnapshot(new ConversationMessageSnapshot
        {
            Id = "m1",
            ProtocolMessageId = "p1",
            ToolCallId = "t1",
            ContentType = "text"
        }, 0);
        Assert.Equal("msg:m1", key);
        Assert.True(TranscriptItemKey.IsRestorable(key));
    }

    [Fact]
    public void PrefersProtocolIdWhenMessageIdMissing()
    {
        var key = TranscriptItemKey.FromSnapshot(new ConversationMessageSnapshot
        {
            Id = "",
            ProtocolMessageId = "p1",
            ContentType = "text",
            TextContent = "streamed body"
        }, 2);
        Assert.Equal("proto:p1", key);
    }

    [Fact]
    public void StreamingBodyDoesNotChangeProtocolKey()
    {
        var a = new ConversationMessageSnapshot { Id = "", ProtocolMessageId = "p1", ContentType = "text", TextContent = "Hel" };
        var b = new ConversationMessageSnapshot { Id = "", ProtocolMessageId = "p1", ContentType = "text", TextContent = "Hello" };
        Assert.Equal(TranscriptItemKey.FromSnapshot(a, 0), TranscriptItemKey.FromSnapshot(b, 0));
    }

    [Fact]
    public void EphemeralKeysAreNotRestorable()
    {
        var key = TranscriptItemKey.FromSnapshot(new ConversationMessageSnapshot
        {
            Id = "",
            ContentType = "text",
            TextContent = "x",
            IsOutgoing = false
        }, 3);
        Assert.StartsWith("ephemeral:", key);
        Assert.False(TranscriptItemKey.IsRestorable(key));
    }
}
