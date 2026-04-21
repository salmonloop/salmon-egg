using System;
using System.Linq;
using System.Threading.Tasks;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Core.Tests.Threading;
using Uno.Extensions.Reactive;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

[Collection("NonParallel")]
public sealed class ConversationCatalogDisplayPresenterTests
{
    [Fact]
    public async Task CatalogAndUnreadAttention_AreCombinedWithoutMutatingCatalogFacts()
    {
        // Arrange
        var catalog = new ConversationCatalogPresenter();
        await using var attentionState = State.Value(new object(), () => ConversationAttentionState.Empty);
        var attentionStore = new ConversationAttentionStore(attentionState);
        using var presenter = new ConversationCatalogDisplayPresenter(catalog, attentionStore, new ImmediateUiDispatcher());

        var first = new ConversationCatalogItem(
            "conv-1",
            "First session",
            @"C:\repo\first",
            new DateTime(2026, 4, 20, 10, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 4, 20, 11, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 4, 20, 12, 0, 0, DateTimeKind.Utc));
        var second = new ConversationCatalogItem(
            "conv-2",
            "Second session",
            @"C:\repo\second",
            new DateTime(2026, 4, 20, 13, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 4, 20, 14, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 4, 20, 15, 0, 0, DateTimeKind.Utc));

        catalog.Refresh([first, second]);

        Assert.Collection(
            presenter.Snapshot,
            item =>
            {
                Assert.Equal("conv-1", item.ConversationId);
                Assert.False(item.HasUnreadAttention);
                Assert.Equal("First session", item.DisplayName);
            },
            item =>
            {
                Assert.Equal("conv-2", item.ConversationId);
                Assert.False(item.HasUnreadAttention);
                Assert.Equal("Second session", item.DisplayName);
            });

        var versionAfterCatalogRefresh = presenter.ConversationListVersion;

        // Act
        await attentionStore.Dispatch(new MarkConversationUnreadAction(
            "conv-2",
            ConversationAttentionSource.AgentMessage,
            new DateTime(2026, 4, 20, 16, 0, 0, DateTimeKind.Utc)));

        await WaitForConditionAsync(() => IsUnread(presenter, "conv-2"));

        await attentionStore.Dispatch(new ClearConversationUnreadAction("conv-2"));

        await WaitForConditionAsync(() => !IsUnread(presenter, "conv-2"));

        // Assert
        Assert.Equal(versionAfterCatalogRefresh, presenter.ConversationListVersion);
        Assert.Equal([first, second], catalog.Snapshot);

        Assert.Collection(
            presenter.Snapshot,
            item =>
            {
                Assert.Equal("conv-1", item.ConversationId);
                Assert.False(item.HasUnreadAttention);
                Assert.Equal("First session", item.DisplayName);
            },
            item =>
            {
                Assert.Equal("conv-2", item.ConversationId);
                Assert.False(item.HasUnreadAttention);
                Assert.Equal("Second session", item.DisplayName);
            });
    }

    private static bool IsUnread(ConversationCatalogDisplayPresenter presenter, string conversationId)
        => presenter.Snapshot.FirstOrDefault(item => string.Equals(item.ConversationId, conversationId, StringComparison.Ordinal))?.HasUnreadAttention == true;

    private static async Task WaitForConditionAsync(Func<bool> predicate, int attempts = 50, int delayMs = 10)
    {
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            if (predicate())
            {
                return;
            }

            await Task.Delay(delayMs);
        }

        Assert.True(predicate());
    }
}
