using System;
using System.Collections.ObjectModel;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using SalmonEgg.Domain.Interfaces.Storage;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Domain.Models.ConversationPreview;
using SalmonEgg.Presentation.ViewModels.Chat.Transcript;
using SalmonEgg.Presentation.ViewModels.Chat;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public partial class ChatViewModelTests
{
    [Fact]
    public async Task ApplyProjection_WhenSessionChanges_ReplacesMessageHistory()
    {
        await using var fixture = CreateViewModel();
        await fixture.ViewModel.RestoreAsync();

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Transcript =
            [
                new ConversationMessageSnapshot
                {
                    Id = "message-1",
                    Timestamp = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
                    IsOutgoing = false,
                    ContentType = "text",
                    TextContent = "hello"
                }
            ]
        });

        var originalHistory = fixture.ViewModel.MessageHistory;

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-2",
            Transcript =
            [
                new ConversationMessageSnapshot
                {
                    Id = "message-2",
                    Timestamp = new DateTime(2026, 3, 3, 0, 0, 0, DateTimeKind.Utc),
                    IsOutgoing = false,
                    ContentType = "text",
                    TextContent = "world"
                }
            ]
        });

        Assert.NotSame(originalHistory, fixture.ViewModel.MessageHistory);
        Assert.Collection(
            fixture.ViewModel.MessageHistory,
            message =>
            {
                Assert.Equal("message-2", message.Id);
                Assert.Equal("world", message.TextContent);
            });
    }

    [Fact]
    public async Task ApplyProjection_WhenSessionStaysSame_DiffsMessageHistory()
    {
        await using var fixture = CreateViewModel();
        await fixture.ViewModel.RestoreAsync();

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Transcript =
            [
                new ConversationMessageSnapshot
                {
                    Id = "message-1",
                    Timestamp = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
                    IsOutgoing = false,
                    ContentType = "text",
                    TextContent = "hello"
                }
            ]
        });

        ObservableCollection<ChatMessageViewModel> originalHistory = fixture.ViewModel.MessageHistory;

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Transcript =
            [
                new ConversationMessageSnapshot
                {
                    Id = "message-1",
                    Timestamp = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
                    IsOutgoing = false,
                    ContentType = "text",
                    TextContent = "hello updated"
                },
                new ConversationMessageSnapshot
                {
                    Id = "message-2",
                    Timestamp = new DateTime(2026, 3, 3, 0, 0, 0, DateTimeKind.Utc),
                    IsOutgoing = true,
                    ContentType = "text",
                    TextContent = "follow up"
                }
            ]
        });

        Assert.Same(originalHistory, fixture.ViewModel.MessageHistory);
        Assert.Collection(
            fixture.ViewModel.MessageHistory,
            message =>
            {
                Assert.Equal("message-1", message.Id);
                Assert.Equal("hello updated", message.TextContent);
            },
            message =>
            {
                Assert.Equal("message-2", message.Id);
                Assert.Equal("follow up", message.TextContent);
            });
    }

    [Fact]
    public async Task ApplyProjection_WhenHydratingEmptyTranscript_UpdatesVisibleTranscriptOwner()
    {
        await using var fixture = CreateViewModel();
        await fixture.ViewModel.RestoreAsync();

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Transcript =
            [
                new ConversationMessageSnapshot
                {
                    Id = "message-1",
                    Timestamp = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
                    IsOutgoing = false,
                    ContentType = "text",
                    TextContent = "stale transcript"
                }
            ]
        });

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-2",
            Transcript = [],
            IsHydrating = true
        });

        Assert.Equal("conv-2", fixture.ViewModel.CurrentSessionId);
        Assert.Empty(fixture.ViewModel.MessageHistory);
        Assert.False(fixture.ViewModel.HasVisibleTranscriptContent);
        Assert.True(fixture.ViewModel.ShouldShowBlockingLoadingMask);
        Assert.DoesNotContain(
            fixture.ViewModel.MessageHistory,
            message => string.Equals(message.TextContent, "stale transcript", StringComparison.Ordinal));
    }
}

public sealed class ChatTranscriptProjectionCoordinatorUnitTests
{
    [Fact]
    public async Task ApplyProjection_WhenSessionChanges_ReplacesCollectionAndSavesPreviewOnce()
    {
        var previewStore = new Mock<IConversationPreviewStore>();
        previewStore.Setup(store => store.SaveAsync(It.IsAny<ConversationPreviewSnapshot>(), default))
            .Returns(Task.CompletedTask);
        var coordinator = new ChatTranscriptProjectionCoordinator(previewStore.Object);
        var owner = (string?)null;
        var history = new ObservableCollection<ChatMessageViewModel>
        {
            new() { Id = "stale", TextContent = "stale" }
        };
        var context = CreateContext(
            () => history,
            value => history = value,
            (conversationId, hasVisibleTranscript) =>
            {
                owner = hasVisibleTranscript ? conversationId : conversationId;
                return true;
            });

        coordinator.ApplyProjection(
            context,
            "conv-2",
            [
                new ConversationMessageSnapshot
                {
                    Id = "message-1",
                    Timestamp = new DateTime(2026, 3, 3, 0, 0, 0, DateTimeKind.Utc),
                    IsOutgoing = false,
                    ContentType = "text",
                    TextContent = "hello"
                }
            ],
            preparedTranscript: null,
            sessionChanged: true);
        coordinator.UpdatePreviewSnapshot(
            "conv-2",
            [
                new ConversationMessageSnapshot
                {
                    Id = "message-1",
                    Timestamp = new DateTime(2026, 3, 3, 0, 0, 0, DateTimeKind.Utc),
                    IsOutgoing = false,
                    ContentType = "text",
                    TextContent = "hello"
                }
            ],
            isHydrating: false);

        Assert.Collection(
            history,
            message =>
            {
                Assert.Equal("message-1", message.Id);
                Assert.Equal("hello", message.TextContent);
            });
        Assert.Equal("conv-2", owner);
        previewStore.Verify(store => store.SaveAsync(
                It.Is<ConversationPreviewSnapshot>(snapshot => snapshot.ConversationId == "conv-2"),
                default),
            Times.Once);
    }

    [Fact]
    public async Task UpdatePreviewSnapshot_WhenOnlyTranscriptReferenceIsUnchanged_DoesNotResavePreview()
    {
        var previewStore = new Mock<IConversationPreviewStore>();
        previewStore.Setup(store => store.SaveAsync(It.IsAny<ConversationPreviewSnapshot>(), default))
            .Returns(Task.CompletedTask);
        var coordinator = new ChatTranscriptProjectionCoordinator(previewStore.Object);
        var transcript = ImmutableList.Create(
            new ConversationMessageSnapshot
            {
                Id = "message-1",
                Timestamp = new DateTime(2026, 3, 3, 0, 0, 0, DateTimeKind.Utc),
                IsOutgoing = false,
                ContentType = "text",
                TextContent = "hello"
            });

        coordinator.UpdatePreviewSnapshot("conv-1", transcript, isHydrating: false);
        coordinator.UpdatePreviewSnapshot("conv-1", transcript, isHydrating: false);

        previewStore.Verify(store => store.SaveAsync(
                It.Is<ConversationPreviewSnapshot>(snapshot => snapshot.ConversationId == "conv-1"),
                default),
            Times.Once);
    }

    private static ChatTranscriptProjectionContext CreateContext(
        Func<ObservableCollection<ChatMessageViewModel>> getHistory,
        Action<ObservableCollection<ChatMessageViewModel>> setHistory,
        Func<string?, bool, bool> updateVisibleTranscriptConversationId)
        => new()
        {
            GetMessageHistory = getHistory,
            SetMessageHistory = setHistory,
            FromSnapshot = snapshot => new ChatMessageViewModel
            {
                Id = snapshot.Id,
                Timestamp = snapshot.Timestamp,
                IsOutgoing = snapshot.IsOutgoing,
                ContentType = snapshot.ContentType,
                TextContent = snapshot.TextContent
            },
            MatchesSnapshot = static (message, snapshot) =>
                string.Equals(message.Id, snapshot.Id, StringComparison.Ordinal)
                && string.Equals(message.TextContent, snapshot.TextContent, StringComparison.Ordinal),
            UpdateVisibleTranscriptConversationId = updateVisibleTranscriptConversationId,
            RaiseTranscriptStateChanged = static () => { }
        };
}
