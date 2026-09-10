using System.Collections.Immutable;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Presentation.Core.Mvux.Chat;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public partial class ChatViewModelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NewSessionDraftProjection_PendingRead_CannotRestoreAfterClearOrDisposal(bool dispose)
    {
        // Arrange
        var dispatcher = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(dispatcher);
        var ready = new NewSessionDraftState(
            "profile", "/work", "session", "connection", NewSessionDraftPhase.Ready, 1,
            ImmutableList.Create(new ConversationModeOptionSnapshot { ModeId = "code", ModeName = "Code" }), "code",
            ImmutableList<ConversationConfigOptionSnapshot>.Empty, false,
            ImmutableList<ConversationAvailableCommandSnapshot>.Empty, null);
        await AwaitWithSynchronizationContextAsync(dispatcher,
            fixture.DispatchConnectionAsync(new SetNewSessionDraftAction(ready)).AsTask());
        await fixture.ApplyNewSessionDraftProjectionAsync();
        await dispatcher.RunUntilIdleAsync();
        var blocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.ChatStore.ReadState = async () =>
        {
            fixture.ChatStore.ReadState = null;
            blocked.TrySetResult();
            await release.Task.WaitAsync(TestContext.Current.CancellationToken);
            return fixture.ChatStore.LatestState;
        };
        var oldProjection = fixture.ViewModel.ApplyLatestNewSessionDraftProjectionAsync();
        try
        {
            await blocked.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            var changed = 0;
            if (dispose)
            {
                fixture.ViewModel.Dispose();
                fixture.ViewModel.PropertyChanged += (_, _) => changed++;
            }
            else
            {
                await AwaitWithSynchronizationContextAsync(dispatcher,
                    fixture.DispatchConnectionAsync(new ClearNewSessionDraftAction()).AsTask());
                await fixture.ApplyNewSessionDraftProjectionAsync();
            }

            // Act
            release.TrySetResult();
            await AwaitWithSynchronizationContextAsync(dispatcher, oldProjection);
            await dispatcher.RunUntilIdleAsync();

            // Assert
            if (dispose)
            {
                Assert.Equal(0, changed);
            }
            else
            {
                Assert.False(fixture.ViewModel.IsNewSessionDraftReady);
                Assert.Empty(fixture.ViewModel.NewSessionDraftModeOptions);
            }
        }
        finally
        {
            fixture.ChatStore.ReadState = null;
            release.TrySetResult();
            await AwaitWithSynchronizationContextAsync(dispatcher, oldProjection);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NewSessionDraftProjection_DelayedOldState_CannotReplaceNewReadyState(bool replaceConnection)
    {
        // Arrange
        var dispatcher = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(dispatcher);
        var emptyDraft = new NewSessionDraftState(
            "profile", "/work", null, "connection", NewSessionDraftPhase.Creating, 1,
            ImmutableList<ConversationModeOptionSnapshot>.Empty, null,
            ImmutableList<ConversationConfigOptionSnapshot>.Empty, false,
            ImmutableList<ConversationAvailableCommandSnapshot>.Empty, null);
        await AwaitWithSynchronizationContextAsync(dispatcher,
            fixture.DispatchConnectionAsync(new SetSelectedProfileIntentAction("profile")).AsTask());
        await AwaitWithSynchronizationContextAsync(dispatcher,
            fixture.DispatchConnectionAsync(new SetForegroundTransportProfileAction("profile")).AsTask());
        await AwaitWithSynchronizationContextAsync(dispatcher,
            fixture.DispatchConnectionAsync(new SetConnectionInstanceIdAction("connection")).AsTask());
        await AwaitWithSynchronizationContextAsync(dispatcher,
            fixture.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected)).AsTask());
        await AwaitWithSynchronizationContextAsync(dispatcher,
            fixture.DispatchConnectionAsync(new SetNewSessionDraftAction(emptyDraft)).AsTask());
        await fixture.ApplyNewSessionDraftProjectionAsync();
        await dispatcher.RunUntilIdleAsync();

        var blocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.ChatStore.ReadState = async () =>
        {
            fixture.ChatStore.ReadState = null;
            blocked.TrySetResult();
            await release.Task.WaitAsync(TestContext.Current.CancellationToken);
            return fixture.ChatStore.LatestState;
        };
        var oldProjection = fixture.ViewModel.ApplyLatestNewSessionDraftProjectionAsync();
        try
        {
            await blocked.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            var connection = replaceConnection ? "replacement" : "connection";
            if (replaceConnection)
            {
                await AwaitWithSynchronizationContextAsync(dispatcher,
                    fixture.DispatchConnectionAsync(new SetConnectionInstanceIdAction(connection)).AsTask());
            }
            var ready = emptyDraft with
            {
                ConnectionInstanceId = connection,
                Phase = NewSessionDraftPhase.Ready,
                Version = 2,
                RemoteSessionId = "ready-session",
                AvailableModes = ImmutableList.Create(new ConversationModeOptionSnapshot { ModeId = "code", ModeName = "Code" }),
                SelectedModeId = "code"
            };
            await AwaitWithSynchronizationContextAsync(dispatcher,
                fixture.DispatchConnectionAsync(new SetNewSessionDraftAction(ready)).AsTask());
            await fixture.ApplyNewSessionDraftProjectionAsync();
            await dispatcher.RunUntilIdleAsync();
            Assert.True(fixture.ViewModel.IsNewSessionDraftReady);

            // Act: let the old projection finish only after the latest state is visible.
            release.TrySetResult();
            await AwaitWithSynchronizationContextAsync(dispatcher, oldProjection);
            await dispatcher.RunUntilIdleAsync();

            // Assert
            Assert.True(fixture.ViewModel.IsNewSessionDraftReady);
            Assert.False(fixture.ViewModel.IsNewSessionDraftLoading);
            Assert.Equal("code", Assert.Single(fixture.ViewModel.NewSessionDraftModeOptions).ModeId);
            Assert.Equal("code", fixture.ViewModel.SelectedNewSessionDraftMode?.ModeId);
        }
        finally
        {
            fixture.ChatStore.ReadState = null;
            release.TrySetResult();
            await AwaitWithSynchronizationContextAsync(dispatcher, oldProjection);
        }
    }
}
