using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using SalmonEgg.Domain.Models.Content;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Domain.Models.Protocol;
using SalmonEgg.Domain.Models.Tool;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Services.Chat;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public sealed class OutgoingUserMessageProjectorTests
{
    private readonly OutgoingUserMessageProjector _projector = new();

    [Fact]
    public void ResolveAuthoritativeProjection_ReusesPendingOptimisticOutgoingMessageWhenAuthoritativeIdArrivesEarly()
    {
        var activeTurn = CreateActiveTurn();
        var optimistic = CreateOutgoingSnapshot("local-1", "hello");
        var transcript = ImmutableList.Create(optimistic);

        var projection = _projector.ResolveAuthoritativeProjection(
            transcript,
            new UserMessageUpdate(new TextContentBlock("hello"))
            {
                MessageId = "server-auth-1"
            },
            activeTurn);

        Assert.Same(optimistic, projection.ExistingSnapshot);
        Assert.Equal("server-auth-1", projection.ProtocolMessageId);
    }

    [Fact]
    public void ResolveAuthoritativeProjection_PrefersExistingMessageMatchedByProtocolMessageId()
    {
        var authoritative = CreateOutgoingSnapshot("local-1", "hello", "server-auth-1");
        var transcript = ImmutableList.Create(authoritative);

        var projection = _projector.ResolveAuthoritativeProjection(
            transcript,
            new UserMessageUpdate(new TextContentBlock("hello"))
            {
                MessageId = "server-auth-1"
            },
            activeTurn: null);

        Assert.Same(authoritative, projection.ExistingSnapshot);
        Assert.Equal("server-auth-1", projection.ProtocolMessageId);
    }

    private static ActiveTurnState CreateActiveTurn()
        => new(
            "conv-1",
            "turn-1",
            ChatTurnPhase.WaitingForAgent,
            DateTime.UtcNow,
            DateTime.UtcNow,
            PendingUserMessageLocalId: "local-1",
            PendingUserProtocolMessageId: "client-request-1",
            PendingUserMessageText: "hello");

    private static ConversationMessageSnapshot CreateOutgoingSnapshot(
        string id,
        string text,
        string? protocolMessageId = null)
        => new()
        {
            Id = id,
            Timestamp = new DateTime(2026, 5, 4, 0, 0, 0, DateTimeKind.Utc),
            IsOutgoing = true,
            ContentType = "text",
            TextContent = text,
            ProtocolMessageId = protocolMessageId,
            ToolCallContent = new List<ToolCallContent>()
        };
}
