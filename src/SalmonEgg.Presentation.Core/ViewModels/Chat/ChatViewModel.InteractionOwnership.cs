using System;
using System.Threading.Tasks;
using SalmonEgg.Acp.Client;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.ViewModels.Chat.Elicitation;
using SalmonEgg.Presentation.ViewModels.Chat.Interactions;

namespace SalmonEgg.Presentation.ViewModels.Chat;

public partial class ChatViewModel
{
    private bool IsInteractionBindingCurrent(AcpSessionEventSource source, ConversationBindingSlice binding)
        // Consent keeps the browser's synchronous user activation while checking the same
        // connection identity used by every background event, not the foreground selection.
        => IsCurrentEventSource(source) && source.Service.IsConnected
            && string.Equals(source.ProfileId, binding.ProfileId, StringComparison.Ordinal)
            && _chatStore.ReadCommittedState()?.ResolveBinding(binding.ConversationId) == binding;

    private void ReconcileElicitationBindings()
    {
        foreach (var request in _panelStateCoordinator.GetElicitationRequests())
        {
            request.ReconcileBinding();
        }
    }

    private void AttachElicitationOwnership(
        ElicitationRequestViewModel viewModel, AcpSessionEventSource source,
        ConversationBindingSlice binding, ElicitationRequestEventArgs request)
    {
        viewModel.BindToConversation(
            () => IsInteractionBindingCurrent(source, binding),
            () => CancelBoundElicitationAsync(viewModel, source, binding, request));
    }

    private async Task<bool> CancelBoundElicitationAsync(
        ElicitationRequestViewModel viewModel, AcpSessionEventSource source,
        ConversationBindingSlice binding, ElicitationRequestEventArgs request)
    {
        var sent = await ChatInteractionEventBridge.CancelUndisplayedElicitationAsync(request, Logger).ConfigureAwait(false);
        if (sent) return true;
        await PostToUiAsync(async () =>
        {
            if (!IsCurrentEventSource(source) || !request.State.CanCancel) return;
            if (IsChatShellVisibleForRemoteUi
                && ReferenceEquals(_panelStateCoordinator.GetPendingElicitationRequest(binding.ConversationId), viewModel)) return;
            await _acpConnectionCommands.DisconnectAfterInteractionFailureAsync(source.Service, this,
                ResolveLocalizerText("Elicitation_CancellationFailedDisconnected",
                    "Could not cancel a request that cannot be displayed. The connection was closed. Reconnect to the agent."))
                .ConfigureAwait(true);
        }).ConfigureAwait(false);
        return false;
    }
}
