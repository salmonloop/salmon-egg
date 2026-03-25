using System;
using System.Collections.Immutable;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Presentation.Core.Mvux.Chat;

namespace SalmonEgg.Presentation.Core.Services.Chat;

public interface IChatStateProjector
{
    ChatUiProjection Apply(
        ChatState storeState,
        ChatConnectionState connectionState,
        string? hydratedConversationId,
        ConversationRemoteBindingState? binding);
}

public sealed class ChatStateProjector : IChatStateProjector
{
    public ChatUiProjection Apply(
        ChatState storeState,
        ChatConnectionState connectionState,
        string? hydratedConversationId,
        ConversationRemoteBindingState? binding)
    {
        ArgumentNullException.ThrowIfNull(storeState);
        var selectedProfileId = connectionState.SelectedProfileId;
        var isConnecting = connectionState.Phase == ConnectionPhase.Connecting;
        var isConnected = connectionState.Phase == ConnectionPhase.Connected;
        var isInitializing = connectionState.Phase == ConnectionPhase.Connecting;
        var connectionStatus = connectionState.Phase == ConnectionPhase.Connected ? "Connected" : "Disconnected";
        var connectionError = connectionState.Error;
        var isAuthenticationRequired = connectionState.IsAuthenticationRequired;
        var authenticationHintMessage = connectionState.AuthenticationHintMessage;

        var activeTurn = GetVisibleActiveTurn(storeState.ActiveTurn, hydratedConversationId);
        var isTurnStatusVisible = activeTurn is not null && activeTurn.Phase != ChatTurnPhase.Completed;
        var turnStatusText = GetTurnStatusText(activeTurn);
        var isTurnStatusRunning = activeTurn is not null
            && activeTurn.Phase is not ChatTurnPhase.Completed
            && activeTurn.Phase is not ChatTurnPhase.Failed
            && activeTurn.Phase is not ChatTurnPhase.Cancelled;

        return new ChatUiProjection(
            HydratedConversationId: hydratedConversationId,
            SelectedProfileId: selectedProfileId,
            RemoteSessionId: binding?.RemoteSessionId,
            IsSessionActive: !string.IsNullOrWhiteSpace(hydratedConversationId),
            IsPromptInFlight: storeState.IsPromptInFlight,
            IsConnecting: isConnecting,
            IsConnected: isConnected,
            IsInitializing: isInitializing,
            ConnectionStatus: connectionStatus,
            ConnectionError: connectionError,
            IsAuthenticationRequired: isAuthenticationRequired,
            AuthenticationHintMessage: authenticationHintMessage,
            ConnectionGeneration: connectionState.Generation,
            AgentName: storeState.AgentName,
            AgentVersion: storeState.AgentVersion,
            CurrentPrompt: storeState.DraftText ?? string.Empty,
            Transcript: storeState.Transcript ?? ImmutableList<ConversationMessageSnapshot>.Empty,
            ShowPlanPanel: storeState.ShowPlanPanel,
            PlanTitle: storeState.PlanTitle,
            PlanEntries: storeState.PlanEntries ?? ImmutableList<ConversationPlanEntrySnapshot>.Empty,
            AvailableModes: storeState.AvailableModes ?? ImmutableList<ConversationModeOptionSnapshot>.Empty,
            SelectedModeId: storeState.SelectedModeId,
            ConfigOptions: storeState.ConfigOptions ?? ImmutableList<ConversationConfigOptionSnapshot>.Empty,
            ShowConfigOptionsPanel: storeState.ShowConfigOptionsPanel,
            IsTurnStatusVisible: isTurnStatusVisible,
            TurnStatusText: turnStatusText,
            IsTurnStatusRunning: isTurnStatusRunning,
            TurnPhase: activeTurn?.Phase);
    }

    private static ActiveTurnState? GetVisibleActiveTurn(ActiveTurnState? activeTurn, string? hydratedConversationId)
    {
        if (activeTurn is null || string.IsNullOrWhiteSpace(hydratedConversationId))
        {
            return null;
        }

        return string.Equals(activeTurn.ConversationId, hydratedConversationId, StringComparison.Ordinal)
            ? activeTurn
            : null;
    }

    private static string GetTurnStatusText(ActiveTurnState? turn)
    {
        if (turn == null) return string.Empty;
        return turn.Phase switch
        {
            ChatTurnPhase.CreatingRemoteSession => "Starting session...",
            ChatTurnPhase.WaitingForAgent => "Waiting for agent...",
            ChatTurnPhase.Thinking => "Thinking...",
            ChatTurnPhase.ToolPending => "Preparing tool call...",
            ChatTurnPhase.ToolRunning => $"Running tool: {turn.ToolTitle ?? "..."}",
            ChatTurnPhase.Responding => "Responding...",
            ChatTurnPhase.Completed => "Completed",
            ChatTurnPhase.Failed => $"Failed: {turn.FailureMessage ?? "Unknown error"}",
            ChatTurnPhase.Cancelled => "Cancelled",
            _ => string.Empty
        };
    }
}

public sealed record ChatUiProjection(
    string? HydratedConversationId,
    string? SelectedProfileId,
    string? RemoteSessionId,
    bool IsSessionActive,
    bool IsPromptInFlight,
    bool IsConnecting,
    bool IsConnected,
    bool IsInitializing,
    string ConnectionStatus,
    string? ConnectionError,
    bool IsAuthenticationRequired,
    string? AuthenticationHintMessage,
    long ConnectionGeneration,
    string? AgentName,
    string? AgentVersion,
    string CurrentPrompt,
    IImmutableList<ConversationMessageSnapshot> Transcript,
    bool ShowPlanPanel,
    string? PlanTitle,
    IReadOnlyList<ConversationPlanEntrySnapshot> PlanEntries,
    IReadOnlyList<ConversationModeOptionSnapshot> AvailableModes,
    string? SelectedModeId,
    IReadOnlyList<ConversationConfigOptionSnapshot> ConfigOptions,
    bool ShowConfigOptionsPanel,
    bool IsTurnStatusVisible,
    string TurnStatusText,
    bool IsTurnStatusRunning,
    ChatTurnPhase? TurnPhase);
