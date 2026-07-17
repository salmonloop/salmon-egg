using System;
using System.Threading.Tasks;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Models.Navigation;

namespace SalmonEgg.Presentation.ViewModels.Chat.Activation;

internal sealed class ConversationActivationOutcomePublisher
{
    private readonly IShellNavigationRuntimeState? _runtimeState;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly Func<bool> _isChatShellVisible;
    private readonly Func<long, bool> _isLatestActivationVersion;

    public ConversationActivationOutcomePublisher(
        IShellNavigationRuntimeState? runtimeState,
        IUiDispatcher uiDispatcher,
        Func<bool> isChatShellVisible,
        Func<long, bool> isLatestActivationVersion)
    {
        _runtimeState = runtimeState;
        _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
        _isChatShellVisible = isChatShellVisible ?? throw new ArgumentNullException(nameof(isChatShellVisible));
        _isLatestActivationVersion = isLatestActivationVersion ?? throw new ArgumentNullException(nameof(isLatestActivationVersion));
    }

    public bool CanPublish(long? activationVersion)
    {
        if (!_isChatShellVisible())
        {
            return false;
        }

        if (_runtimeState?.PendingShellContent is { } pendingShellContent
            && pendingShellContent != ShellNavigationContent.Chat)
        {
            return false;
        }

        return !activationVersion.HasValue || _isLatestActivationVersion(activationVersion.Value);
    }

    public Task TryPublishFailureAsync(
        string conversationId,
        long? activationVersion,
        long expectedSnapshotVersion,
        string reason,
        string message)
    {
        if (_runtimeState is null || !CanPublish(activationVersion))
        {
            return Task.CompletedTask;
        }

        var expectedActivation = _runtimeState.ActiveSessionActivation;
        if (expectedActivation is null
            || !expectedActivation.Matches(conversationId)
            || expectedActivation.Version != expectedSnapshotVersion)
        {
            return Task.CompletedTask;
        }

        return _uiDispatcher.EnqueueAsync(() =>
        {
            if (!CanPublish(activationVersion))
            {
                return;
            }

            var activeActivation = _runtimeState.ActiveSessionActivation;
            if (activeActivation is null
                || !activeActivation.Matches(conversationId)
                || activeActivation.Version != expectedSnapshotVersion)
            {
                return;
            }

            _runtimeState.ActiveSessionActivation = activeActivation with
            {
                Phase = SessionActivationPhase.Faulted,
                Reason = reason,
                FailureMessage = message
            };
            _runtimeState.IsSessionActivationInProgress = false;
            _runtimeState.ActiveSessionActivationVersion = 0;
        });
    }

    public Task TryPublishPhaseAsync(
        string conversationId,
        long? activationVersion,
        long expectedSnapshotVersion,
        SessionActivationPhase phase,
        string? reason = null)
    {
        if (_runtimeState is null || !CanPublish(activationVersion))
        {
            return Task.CompletedTask;
        }

        return _uiDispatcher.EnqueueAsync(() =>
        {
            if (!CanPublish(activationVersion))
            {
                return;
            }

            var activeActivation = _runtimeState.ActiveSessionActivation;
            if (activeActivation is null
                || !activeActivation.Matches(conversationId)
                || activeActivation.Version != expectedSnapshotVersion)
            {
                return;
            }

            if (phase != SessionActivationPhase.Faulted
                && activeActivation.Phase == SessionActivationPhase.Faulted)
            {
                return;
            }

            if (phase != SessionActivationPhase.Faulted
                && phase < activeActivation.Phase)
            {
                return;
            }

            if (activeActivation.Phase == phase
                && string.Equals(activeActivation.Reason, reason, StringComparison.Ordinal))
            {
                return;
            }

            _runtimeState.ActiveSessionActivation = activeActivation with
            {
                Phase = phase,
                Reason = reason
            };

            var terminal = phase is SessionActivationPhase.Hydrated or SessionActivationPhase.Faulted;
            _runtimeState.IsSessionActivationInProgress = !terminal;
            _runtimeState.ActiveSessionActivationVersion = terminal
                ? 0
                : activeActivation.Version;
        });
    }
}
