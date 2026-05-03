using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SalmonEgg.Presentation.Core.Services;

namespace SalmonEgg.Presentation.ViewModels.Chat.Activation;

internal sealed class ConversationActivationOutcomePublisher
{
    private readonly IShellNavigationRuntimeState? _runtimeState;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly ILogger _logger;
    private readonly Func<bool> _isChatShellVisible;
    private readonly Func<long, bool> _isLatestActivationVersion;
    private readonly Action<string> _setError;

    public ConversationActivationOutcomePublisher(
        IShellNavigationRuntimeState? runtimeState,
        IUiDispatcher uiDispatcher,
        ILogger logger,
        Func<bool> isChatShellVisible,
        Func<long, bool> isLatestActivationVersion,
        Action<string> setError)
    {
        _runtimeState = runtimeState;
        _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _isChatShellVisible = isChatShellVisible ?? throw new ArgumentNullException(nameof(isChatShellVisible));
        _isLatestActivationVersion = isLatestActivationVersion ?? throw new ArgumentNullException(nameof(isLatestActivationVersion));
        _setError = setError ?? throw new ArgumentNullException(nameof(setError));
    }

    public bool CanPublish(long? activationVersion)
    {
        if (!_isChatShellVisible())
        {
            return false;
        }

        return !activationVersion.HasValue || _isLatestActivationVersion(activationVersion.Value);
    }

    public Task TrySetActivationErrorAsync(string conversationId, long? activationVersion, string message)
    {
        if (!CanPublish(activationVersion))
        {
            _logger.LogInformation(
                "Discarding stale conversation activation error because the chat shell no longer owns the latest intent. conversationId={ConversationId} activationVersion={ActivationVersion} message={Message}",
                conversationId,
                activationVersion,
                message);
            return Task.CompletedTask;
        }

        return _uiDispatcher.EnqueueAsync(() => _setError(message));
    }

    public Task TryPublishPhaseAsync(
        string conversationId,
        long? activationVersion,
        SessionActivationPhase phase,
        string? reason = null)
    {
        if (_runtimeState is null || !CanPublish(activationVersion))
        {
            return Task.CompletedTask;
        }

        return _uiDispatcher.EnqueueAsync(() =>
        {
            var activeActivation = _runtimeState.ActiveSessionActivation;
            if (activeActivation is null || !activeActivation.Matches(conversationId))
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
