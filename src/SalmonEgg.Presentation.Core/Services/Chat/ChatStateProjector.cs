using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using Uno.Extensions.Reactive;

namespace SalmonEgg.Presentation.Core.Services.Chat;

public interface IChatStateProjector
{
    ChatUiProjection Apply(
        ChatState storeState,
        string? currentConversationId,
        ConversationRemoteBindingState? binding);
}

public sealed class ChatStateProjector : IChatStateProjector
{
    private readonly bool _usesConnectionStore;
    private readonly IDisposable? _connectionSubscription;
    private ChatConnectionState _connectionState = ChatConnectionState.Empty;

    public ChatStateProjector()
    {
        _usesConnectionStore = false;
    }

    public ChatStateProjector(IChatConnectionStore connectionStore)
    {
        ArgumentNullException.ThrowIfNull(connectionStore);

        _usesConnectionStore = true;
        connectionStore.State.ForEach((state, ct) =>
        {
            if (state is not null)
            {
                Volatile.Write(ref _connectionState, state);
            }

            return ValueTask.CompletedTask;
        }, out _connectionSubscription);
    }

    public ChatUiProjection Apply(
        ChatState storeState,
        string? currentConversationId,
        ConversationRemoteBindingState? binding)
    {
        ArgumentNullException.ThrowIfNull(storeState);

        var selectedProfileId = storeState.SelectedAcpProfileId ?? binding?.BoundProfileId;
        var isConnecting = storeState.IsConnecting;
        var isConnected = string.Equals(storeState.ConnectionStatus, "Connected", StringComparison.Ordinal);
        var isInitializing = storeState.IsInitializing;
        var connectionStatus = storeState.ConnectionStatus;
        var connectionError = storeState.ConnectionError;
        var isAuthenticationRequired = storeState.IsAuthenticationRequired;
        var authenticationHintMessage = storeState.AuthenticationHintMessage;

        if (_usesConnectionStore)
        {
            var connectionState = Volatile.Read(ref _connectionState);
            selectedProfileId = connectionState.SelectedProfileId;
            isConnecting = connectionState.Phase == ConnectionPhase.Connecting;
            isConnected = connectionState.Phase == ConnectionPhase.Connected;
            isInitializing = connectionState.Phase == ConnectionPhase.Connecting;
            connectionStatus = connectionState.Phase == ConnectionPhase.Connected ? "Connected" : "Disconnected";
            connectionError = connectionState.Error;
            isAuthenticationRequired = connectionState.IsAuthenticationRequired;
            authenticationHintMessage = connectionState.AuthenticationHintMessage;
        }

        return new ChatUiProjection(
            SelectedConversationId: currentConversationId,
            SelectedProfileId: selectedProfileId,
            RemoteSessionId: binding?.RemoteSessionId,
            IsSessionActive: !string.IsNullOrWhiteSpace(currentConversationId),
            IsPromptInFlight: storeState.IsPromptInFlight,
            IsThinking: storeState.IsThinking,
            IsConnecting: isConnecting,
            IsConnected: isConnected,
            IsInitializing: isInitializing,
            ConnectionStatus: connectionStatus,
            ConnectionError: connectionError,
            IsAuthenticationRequired: isAuthenticationRequired,
            AuthenticationHintMessage: authenticationHintMessage,
            AgentName: storeState.AgentName,
            AgentVersion: storeState.AgentVersion,
            CurrentPrompt: storeState.DraftText ?? string.Empty,
            Transcript: storeState.Transcript ?? ImmutableList<ConversationMessageSnapshot>.Empty,
            ShowPlanPanel: storeState.ShowPlanPanel,
            PlanTitle: storeState.PlanTitle,
            PlanEntries: storeState.PlanEntries ?? ImmutableList<ConversationPlanEntrySnapshot>.Empty);
    }
}

public sealed record ChatUiProjection(
    string? SelectedConversationId,
    string? SelectedProfileId,
    string? RemoteSessionId,
    bool IsSessionActive,
    bool IsPromptInFlight,
    bool IsThinking,
    bool IsConnecting,
    bool IsConnected,
    bool IsInitializing,
    string ConnectionStatus,
    string? ConnectionError,
    bool IsAuthenticationRequired,
    string? AuthenticationHintMessage,
    string? AgentName,
    string? AgentVersion,
    string CurrentPrompt,
    IImmutableList<ConversationMessageSnapshot> Transcript,
    bool ShowPlanPanel,
    string? PlanTitle,
    IReadOnlyList<ConversationPlanEntrySnapshot> PlanEntries);
