using System;
using System.Threading.Tasks;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Tests.Threading;
using SalmonEgg.Presentation.Models.Navigation;
using SalmonEgg.Presentation.ViewModels.Chat.Activation;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

[Collection("NonParallel")]
public sealed class ConversationActivationOutcomePublisherTests
{
    [Fact]
    public async Task TryPublishFailureAsync_WhenCurrent_StoresTerminalFailureAtomically()
    {
        var runtimeState = CreateRuntimeState("conv-1", version: 7);
        var publisher = CreatePublisher(runtimeState);

        await publisher.TryPublishFailureAsync(
            "conv-1",
            7,
            expectedSnapshotVersion: 7,
            "MissingRemoteSessionId",
            "Failed to load session: no remote binding.");

        var snapshot = Assert.IsType<SessionActivationSnapshot>(runtimeState.ActiveSessionActivation);
        Assert.Equal(SessionActivationPhase.Faulted, snapshot.Phase);
        Assert.Equal("MissingRemoteSessionId", snapshot.Reason);
        Assert.Equal("Failed to load session: no remote binding.", snapshot.FailureMessage);
        Assert.False(runtimeState.IsSessionActivationInProgress);
        Assert.Equal(0, runtimeState.ActiveSessionActivationVersion);
    }

    [Fact]
    public async Task TryPublishFailureAsync_StoresLocalizationIdentityWithFailureMessage()
    {
        var runtimeState = CreateRuntimeState("conv-1", version: 7);
        var publisher = CreatePublisher(runtimeState);

        await publisher.TryPublishFailureAsync(
            "conv-1",
            7,
            expectedSnapshotVersion: 7,
            "MissingRemoteSessionId",
            "Failed to load session: no remote binding.",
            failureResourceKey: "ChatOperation_LoadSessionMissingActiveBinding",
            failureFallback: "Failed to load session: no remote session binding is available for the active conversation.");

        var snapshot = Assert.IsType<SessionActivationSnapshot>(runtimeState.ActiveSessionActivation);
        Assert.Equal(SessionActivationPhase.Faulted, snapshot.Phase);
        Assert.Equal("Failed to load session: no remote binding.", snapshot.FailureMessage);
        Assert.Equal("ChatOperation_LoadSessionMissingActiveBinding", snapshot.FailureResourceKey);
        Assert.Equal(
            "Failed to load session: no remote session binding is available for the active conversation.",
            snapshot.FailureFallback);
        Assert.Null(snapshot.FailureFormatArgs);
    }

    [Fact]
    public async Task TryPublishFailureAsync_WhenSnapshotOwnerMismatches_DoesNotMutate()
    {
        var runtimeState = CreateRuntimeState("conv-1", version: 7);
        var publisher = CreatePublisher(runtimeState);

        await publisher.TryPublishFailureAsync(
            "conv-2",
            7,
            expectedSnapshotVersion: 7,
            "Failure",
            "Failure message");

        Assert.Equal(SessionActivationPhase.Selected, runtimeState.ActiveSessionActivation?.Phase);
        Assert.Null(runtimeState.ActiveSessionActivation?.FailureMessage);
        Assert.True(runtimeState.IsSessionActivationInProgress);
        Assert.Equal(7, runtimeState.ActiveSessionActivationVersion);
    }

    [Fact]
    public async Task TryPublishFailureAsync_WhenIndependentTokensDiffer_StillPublishesLatestFailure()
    {
        var runtimeState = CreateRuntimeState("conv-1", version: 41);
        var publisher = CreatePublisher(runtimeState);

        await publisher.TryPublishFailureAsync(
            "conv-1",
            activationVersion: 7,
            expectedSnapshotVersion: 41,
            "Failure",
            "Failure message");

        Assert.Equal(SessionActivationPhase.Faulted, runtimeState.ActiveSessionActivation?.Phase);
        Assert.Equal(41, runtimeState.ActiveSessionActivation?.Version);
        Assert.Equal("Failure message", runtimeState.ActiveSessionActivation?.FailureMessage);
    }

    [Fact]
    public async Task TryPublishFailureAsync_WhenSupersededWhileQueued_DoesNotCommit()
    {
        var runtimeState = CreateRuntimeState("conv-1", version: 7);
        var dispatcher = new QueueingUiDispatcher();
        var publisher = new ConversationActivationOutcomePublisher(
            runtimeState,
            dispatcher,
            isChatShellVisible: () => true,
            isLatestActivationVersion: version => version == runtimeState.LatestActivationToken);

        var publication = publisher.TryPublishFailureAsync(
            "conv-1",
            7,
            expectedSnapshotVersion: 7,
            "Failure",
            "Failure message");
        runtimeState.LatestActivationToken = 8;
        runtimeState.ActiveSessionActivationVersion = 8;
        runtimeState.ActiveSessionActivation = new SessionActivationSnapshot(
            "conv-1",
            "project-1",
            8,
            SessionActivationPhase.Selected);

        dispatcher.RunAll();
        await publication;

        Assert.Equal("conv-1", runtimeState.ActiveSessionActivation?.SessionId);
        Assert.Equal(SessionActivationPhase.Selected, runtimeState.ActiveSessionActivation?.Phase);
        Assert.Null(runtimeState.ActiveSessionActivation?.FailureMessage);
    }

    [Fact]
    public async Task TryPublishFailureAsync_WhenSameConversationWasSupersededBeforePublication_DoesNotFaultNewIntent()
    {
        var runtimeState = CreateRuntimeState("conv-1", version: 8);
        var publisher = CreatePublisher(runtimeState);

        await publisher.TryPublishFailureAsync(
            "conv-1",
            activationVersion: 7,
            expectedSnapshotVersion: 7,
            "Failure",
            "Old activation failed");

        Assert.Equal(8, runtimeState.ActiveSessionActivation?.Version);
        Assert.Equal(SessionActivationPhase.Selected, runtimeState.ActiveSessionActivation?.Phase);
        Assert.Null(runtimeState.ActiveSessionActivation?.FailureMessage);
        Assert.True(runtimeState.IsSessionActivationInProgress);
    }

    [Fact]
    public async Task TryPublishPhaseAsync_WhenHydrated_CompletesShellActivation()
    {
        var runtimeState = new ShellNavigationRuntimeStateStore
        {
            CurrentShellContent = ShellNavigationContent.Chat,
            LatestActivationToken = 7,
            ActiveSessionActivationVersion = 7,
            IsSessionActivationInProgress = true,
            ActiveSessionActivation = new SessionActivationSnapshot(
                "conv-1",
                "project-1",
                7,
                SessionActivationPhase.Selected)
        };
        var publisher = CreatePublisher(runtimeState);

        await publisher.TryPublishPhaseAsync(
            "conv-1",
            7,
            expectedSnapshotVersion: 7,
            SessionActivationPhase.Hydrated,
            "LocalConversationReady");

        Assert.Equal(SessionActivationPhase.Hydrated, runtimeState.ActiveSessionActivation?.Phase);
        Assert.Equal("LocalConversationReady", runtimeState.ActiveSessionActivation?.Reason);
        Assert.False(runtimeState.IsSessionActivationInProgress);
        Assert.Equal(0, runtimeState.ActiveSessionActivationVersion);
    }

    [Fact]
    public async Task TryPublishPhaseAsync_WhenActivationIsStale_DoesNotMutateRuntimeState()
    {
        var runtimeState = new ShellNavigationRuntimeStateStore
        {
            CurrentShellContent = ShellNavigationContent.Chat,
            LatestActivationToken = 8,
            ActiveSessionActivationVersion = 8,
            IsSessionActivationInProgress = true,
            ActiveSessionActivation = new SessionActivationSnapshot(
                "conv-1",
                "project-1",
                8,
                SessionActivationPhase.Selected)
        };
        var publisher = new ConversationActivationOutcomePublisher(
            runtimeState,
            new ImmediateUiDispatcher(),
            isChatShellVisible: () => true,
            isLatestActivationVersion: version => version == 8);

        await publisher.TryPublishPhaseAsync(
            "conv-1",
            7,
            expectedSnapshotVersion: 8,
            SessionActivationPhase.Faulted,
            "Timeout");

        Assert.Equal(SessionActivationPhase.Selected, runtimeState.ActiveSessionActivation?.Phase);
        Assert.True(runtimeState.IsSessionActivationInProgress);
        Assert.Equal(8, runtimeState.ActiveSessionActivationVersion);
    }

    [Fact]
    public async Task TryPublishPhaseAsync_WhenFaultedThenHydratedForSameSnapshot_SelfHealsToHydrated()
    {
        // A transient fault (e.g. a recoverable RemoteConnectionNotReady) must not strand the
        // activation banner. When the same latest-intent snapshot subsequently hydrates
        // successfully, the fault self-heals to Hydrated.
        var runtimeState = new ShellNavigationRuntimeStateStore
        {
            CurrentShellContent = ShellNavigationContent.Chat,
            LatestActivationToken = 9,
            ActiveSessionActivationVersion = 0,
            IsSessionActivationInProgress = false,
            ActiveSessionActivation = new SessionActivationSnapshot(
                "conv-1",
                "project-1",
                9,
                SessionActivationPhase.Faulted,
                "RemoteConnectionNotReady")
        };
        var publisher = CreatePublisher(runtimeState);

        await publisher.TryPublishPhaseAsync(
            "conv-1",
            9,
            expectedSnapshotVersion: 9,
            SessionActivationPhase.Hydrated,
            "Hydrated");

        Assert.Equal(SessionActivationPhase.Hydrated, runtimeState.ActiveSessionActivation?.Phase);
        Assert.Equal("Hydrated", runtimeState.ActiveSessionActivation?.Reason);
        Assert.False(runtimeState.IsSessionActivationInProgress);
        Assert.Equal(0, runtimeState.ActiveSessionActivationVersion);
    }

    [Fact]
    public async Task TryPublishPhaseAsync_WhenFaultedThenStaleLowerPhase_KeepsFault()
    {
        // Only a genuine success terminal recovers a fault. A late, lower-ordinal phase
        // (e.g. a straggling SelectingConversation) must not silently un-fault the snapshot.
        var runtimeState = new ShellNavigationRuntimeStateStore
        {
            CurrentShellContent = ShellNavigationContent.Chat,
            LatestActivationToken = 9,
            ActiveSessionActivationVersion = 0,
            IsSessionActivationInProgress = false,
            ActiveSessionActivation = new SessionActivationSnapshot(
                "conv-1",
                "project-1",
                9,
                SessionActivationPhase.Faulted,
                "RemoteConnectionNotReady")
        };
        var publisher = CreatePublisher(runtimeState);

        await publisher.TryPublishPhaseAsync(
            "conv-1",
            9,
            expectedSnapshotVersion: 9,
            SessionActivationPhase.SelectingConversation,
            "SelectingConversation");

        Assert.Equal(SessionActivationPhase.Faulted, runtimeState.ActiveSessionActivation?.Phase);
        Assert.Equal("RemoteConnectionNotReady", runtimeState.ActiveSessionActivation?.Reason);
    }

    [Fact]
    public async Task TryPublishPhaseAsync_WhenNonChatNavigationIsPending_DoesNotPublishRemoteOutcome()
    {
        var runtimeState = new ShellNavigationRuntimeStateStore
        {
            CurrentShellContent = ShellNavigationContent.Chat,
            PendingShellContent = ShellNavigationContent.Start,
            LatestActivationToken = 10,
            ActiveSessionActivationVersion = 10,
            IsSessionActivationInProgress = true,
            ActiveSessionActivation = new SessionActivationSnapshot(
                "conv-1",
                "project-1",
                10,
                SessionActivationPhase.Selected)
        };
        var publisher = CreatePublisher(runtimeState);

        await publisher.TryPublishPhaseAsync(
            "conv-1",
            10,
            expectedSnapshotVersion: 10,
            SessionActivationPhase.Faulted,
            "RemoteLoadFailed");

        Assert.Equal(SessionActivationPhase.Selected, runtimeState.ActiveSessionActivation?.Phase);
        Assert.True(runtimeState.IsSessionActivationInProgress);
        Assert.Equal(10, runtimeState.ActiveSessionActivationVersion);
    }

    private static ConversationActivationOutcomePublisher CreatePublisher(
        IShellNavigationRuntimeState runtimeState)
        => new(
            runtimeState,
            new ImmediateUiDispatcher(),
            isChatShellVisible: () => true,
            isLatestActivationVersion: _ => true);

    private static ShellNavigationRuntimeStateStore CreateRuntimeState(string conversationId, long version)
        => new()
        {
            CurrentShellContent = ShellNavigationContent.Chat,
            LatestActivationToken = version,
            ActiveSessionActivationVersion = version,
            IsSessionActivationInProgress = true,
            ActiveSessionActivation = new SessionActivationSnapshot(
                conversationId,
                "project-1",
                version,
                SessionActivationPhase.Selected)
        };
}
