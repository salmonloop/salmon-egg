using System;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Acp.Client;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.ViewModels.Chat.Elicitation;
using SalmonEgg.Presentation.ViewModels.Chat.Interactions;

namespace SalmonEgg.Presentation.ViewModels.Chat;

public partial class ChatViewModel
{
    private sealed record InteractionRequestSource(
        IChatService Service, int ForegroundGeneration, AcpAuthoritativeConnectionSnapshot? Connection);

    private InteractionRequestSource CaptureInteractionSource(IChatService service, int foregroundGeneration)
        => new(service, foregroundGeneration,
            _authoritativeConnectionResolver.TryResolveSourceConnection(service, out var connection) ? connection : null);

    private bool IsInteractionSourceActive(InteractionRequestSource source)
        => !_disposed && source.ForegroundGeneration == Volatile.Read(ref _foregroundChatServiceGeneration)
            && source.Service.IsConnected;

    private bool IsInteractionConnectionCurrent(InteractionRequestSource source)
        => IsInteractionSourceActive(source) && source.Connection is { } connection
            && _authoritativeConnectionResolver.IsSourceConnectionCurrent(connection);

    private string? ResolveInteractionConversation(ChatState state, string? remoteSessionId, InteractionRequestSource source)
        => source.Connection?.ProfileId is { } profileId && !string.IsNullOrWhiteSpace(remoteSessionId)
            ? _authoritativeRemoteSessionRouter.ResolveConversationId(state, remoteSessionId, profileId)
            : null;

    private bool IsInteractionBindingCurrent(InteractionRequestSource source, ConversationBindingSlice binding)
        => IsInteractionConnectionCurrent(source)
            && string.Equals(source.Connection?.ProfileId, binding.ProfileId, StringComparison.Ordinal)
            && _chatStore.ReadCommittedState()?.ResolveBinding(binding.ConversationId) == binding;

    private void ReconcileElicitationBindings()
    {
        foreach (var request in _panelStateCoordinator.GetElicitationRequests())
        {
            request.ReconcileBinding();
        }
    }

    private void AttachElicitationOwnership(
        ElicitationRequestViewModel viewModel, InteractionRequestSource source,
        ConversationBindingSlice binding, ElicitationRequestEventArgs request)
    {
        viewModel.BindToConversation(
            () => IsInteractionBindingCurrent(source, binding),
            () => CancelBoundElicitationAsync(viewModel, source, binding, request));
    }

    private async Task<bool> CancelBoundElicitationAsync(
        ElicitationRequestViewModel viewModel, InteractionRequestSource source,
        ConversationBindingSlice binding, ElicitationRequestEventArgs request)
    {
        var sent = await ChatInteractionEventBridge.CancelUndisplayedElicitationAsync(request, Logger).ConfigureAwait(false);
        if (sent) return true;
        await PostToUiAsync(async () =>
        {
            if (!IsInteractionSourceActive(source) || !request.State.CanCancel) return;
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
