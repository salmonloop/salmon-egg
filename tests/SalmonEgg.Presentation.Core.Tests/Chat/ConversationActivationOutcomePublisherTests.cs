using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
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
        string? errorMessage = null;
        var publisher = CreatePublisher(runtimeState, message => errorMessage = message);

        await publisher.TryPublishPhaseAsync(
            "conv-1",
            7,
            SessionActivationPhase.Hydrated,
            "LocalConversationReady");

        Assert.Equal(SessionActivationPhase.Hydrated, runtimeState.ActiveSessionActivation?.Phase);
        Assert.Equal("LocalConversationReady", runtimeState.ActiveSessionActivation?.Reason);
        Assert.False(runtimeState.IsSessionActivationInProgress);
        Assert.Equal(0, runtimeState.ActiveSessionActivationVersion);
        Assert.Null(errorMessage);
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
        string? errorMessage = null;
        var publisher = new ConversationActivationOutcomePublisher(
            runtimeState,
            new ImmediateUiDispatcher(),
            NullLogger<ConversationActivationOutcomePublisher>.Instance,
            isChatShellVisible: () => true,
            isLatestActivationVersion: version => version == 8,
            setError: message => errorMessage = message);

        await publisher.TryPublishPhaseAsync(
            "conv-1",
            7,
            SessionActivationPhase.Faulted,
            "Timeout");
        await publisher.TrySetActivationErrorAsync(
            "conv-1",
            7,
            "Failed to load session: timeout.");

        Assert.Equal(SessionActivationPhase.Selected, runtimeState.ActiveSessionActivation?.Phase);
        Assert.True(runtimeState.IsSessionActivationInProgress);
        Assert.Equal(8, runtimeState.ActiveSessionActivationVersion);
        Assert.Null(errorMessage);
    }

    [Fact]
    public async Task TryPublishPhaseAsync_WhenActivationAlreadyFaulted_DoesNotOverwriteTerminalFault()
    {
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
        var publisher = CreatePublisher(runtimeState, _ => { });

        await publisher.TryPublishPhaseAsync(
            "conv-1",
            9,
            SessionActivationPhase.Hydrated,
            "Hydrated");

        Assert.Equal(SessionActivationPhase.Faulted, runtimeState.ActiveSessionActivation?.Phase);
        Assert.Equal("RemoteConnectionNotReady", runtimeState.ActiveSessionActivation?.Reason);
        Assert.False(runtimeState.IsSessionActivationInProgress);
        Assert.Equal(0, runtimeState.ActiveSessionActivationVersion);
    }

    private static ConversationActivationOutcomePublisher CreatePublisher(
        IShellNavigationRuntimeState runtimeState,
        Action<string> setError)
        => new(
            runtimeState,
            new ImmediateUiDispatcher(),
            NullLogger<ConversationActivationOutcomePublisher>.Instance,
            isChatShellVisible: () => true,
            isLatestActivationVersion: _ => true,
            setError: setError);
}
