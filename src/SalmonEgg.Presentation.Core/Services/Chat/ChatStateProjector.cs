using System;
using System.Collections.Immutable;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Presentation.Core.Mvux.Chat;

namespace SalmonEgg.Presentation.Core.Services.Chat;

public interface IChatStateProjector
{
    ChatUiProjection Apply(
        ChatState storeState,
        ConversationRemoteBindingState? binding);
}

public sealed class ChatStateProjector : IChatStateProjector
{
    public ChatUiProjection Apply(
        ChatState storeState,
        ConversationRemoteBindingState? binding)
    {
        ArgumentNullException.ThrowIfNull(storeState);

        return new ChatUiProjection(
            SelectedConversationId: storeState.SelectedConversationId,
            SelectedProfileId: storeState.SelectedAcpProfileId ?? binding?.BoundProfileId,
            RemoteSessionId: binding?.RemoteSessionId,
            IsSessionActive: !string.IsNullOrWhiteSpace(storeState.SelectedConversationId),
            IsPromptInFlight: storeState.IsPromptInFlight,
            IsThinking: storeState.IsThinking,
            IsConnecting: storeState.IsConnecting,
            IsConnected: string.Equals(storeState.ConnectionStatus, "Connected", StringComparison.Ordinal),
            IsInitializing: storeState.IsInitializing,
            ConnectionStatus: storeState.ConnectionStatus,
            ConnectionError: storeState.ConnectionError,
            IsAuthenticationRequired: storeState.IsAuthenticationRequired,
            AuthenticationHintMessage: storeState.AuthenticationHintMessage,
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
