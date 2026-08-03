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
        string message,
        string? failureResourceKey = null,
        string? failureFallback = null,
        object[]? failureFormatArgs = null)
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
                FailureMessage = message,
                FailureResourceKey = failureResourceKey,
                FailureFallback = failureFallback,
                FailureFormatArgs = failureFormatArgs
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

            if (activeActivation.Phase == SessionActivationPhase.Faulted)
            {
                // A prior fault self-heals only on a genuine success terminal (Hydrated) for the
                // same latest-intent snapshot. CanPublish + the Version match above already restrict
                // this to the current owner's own recovery, so a superseded/stale callback cannot
                // un-fault it. Any non-Hydrated phase (e.g. a late SelectingConversation) is stale
                // relative to the fault and must not silently clear it.
                if (phase != SessionActivationPhase.Hydrated)
                {
                    return;
                }
            }
            else if (phase != SessionActivationPhase.Faulted
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
