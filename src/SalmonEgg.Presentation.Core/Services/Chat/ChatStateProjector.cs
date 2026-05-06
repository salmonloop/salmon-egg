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
    private readonly ChatSessionToolingProjector _sessionToolingProjector;
    private readonly TranscriptProjectionRestoreTokenProjector _restoreTokenProjector;

    public ChatStateProjector()
        : this(new ChatSessionToolingProjector(), new TranscriptProjectionRestoreTokenProjector())
    {
    }

    public ChatStateProjector(
        ChatSessionToolingProjector sessionToolingProjector,
        TranscriptProjectionRestoreTokenProjector restoreTokenProjector)
    {
        _sessionToolingProjector = sessionToolingProjector ?? throw new ArgumentNullException(nameof(sessionToolingProjector));
        _restoreTokenProjector = restoreTokenProjector ?? throw new ArgumentNullException(nameof(restoreTokenProjector));
    }

    public ChatUiProjection Apply(
        ChatState storeState,
        ChatConnectionState connectionState,
        string? hydratedConversationId,
        ConversationRemoteBindingState? binding)
    {
        ArgumentNullException.ThrowIfNull(storeState);
        var settingsSelectedProfileId = connectionState.SettingsSelectedProfileId;
        var foregroundTransportProfileId = connectionState.ForegroundTransportProfileId;
        var chatOwnerProfileId = binding?.BoundProfileId;
        var effectiveDisplayProfileId = !string.IsNullOrWhiteSpace(chatOwnerProfileId)
            ? chatOwnerProfileId
            : foregroundTransportProfileId;
        var displayAgentName = string.Equals(storeState.AgentProfileId, effectiveDisplayProfileId, StringComparison.Ordinal)
            ? storeState.AgentName
            : null;
        var displayAgentVersion = string.Equals(storeState.AgentProfileId, effectiveDisplayProfileId, StringComparison.Ordinal)
            ? storeState.AgentVersion
            : null;
        var isConnecting = connectionState.Phase == ConnectionPhase.Connecting;
        var isConnected = connectionState.Phase == ConnectionPhase.Connected;
        var isInitializing = connectionState.Phase == ConnectionPhase.Initializing;
        var connectionStatus = connectionState.Phase == ConnectionPhase.Connected ? "Connected" : 
                               connectionState.Phase == ConnectionPhase.Initializing ? "Initializing..." :
                               connectionState.Phase == ConnectionPhase.Connecting ? "Connecting..." : "Disconnected";
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
        var contentSlice = storeState.ResolveContentSlice(hydratedConversationId);
        var sessionStateSlice = storeState.ResolveSessionStateSlice(hydratedConversationId);
        var toolingProjection = _sessionToolingProjector.Project(storeState, hydratedConversationId);
        var transcript = contentSlice?.Transcript
            ?? storeState.Transcript
            ?? ImmutableList<ConversationMessageSnapshot>.Empty;
        var planEntries = contentSlice?.PlanEntries
            ?? storeState.PlanEntries
            ?? ImmutableList<ConversationPlanEntrySnapshot>.Empty;
        var restoreProjection = _restoreTokenProjector.Project(
            conversationId: hydratedConversationId ?? string.Empty,
            transcript,
            firstVisibleIndex: transcript.Count > 0 ? transcript.Count - 1 : -1,
            relativeOffsetWithinItem: 0d);

        return new ChatUiProjection(
            HydratedConversationId: hydratedConversationId,
            ChatOwnerProfileId: chatOwnerProfileId,
            SettingsSelectedProfileId: !string.IsNullOrWhiteSpace(settingsSelectedProfileId)
                ? settingsSelectedProfileId
                : effectiveDisplayProfileId,
            ForegroundTransportProfileId: foregroundTransportProfileId,
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
            ConnectionInstanceId: connectionState.ConnectionInstanceId,
            ConnectionGeneration: connectionState.Generation,
            AgentName: displayAgentName,
            AgentVersion: displayAgentVersion,
            CurrentPrompt: storeState.DraftText ?? string.Empty,
            Transcript: transcript,
            RestoreProjection: restoreProjection,
            ShowPlanPanel: contentSlice?.ShowPlanPanel ?? storeState.ShowPlanPanel,
            PlanTitle: contentSlice?.PlanTitle ?? storeState.PlanTitle,
            PlanEntries: planEntries,
            AvailableModes: toolingProjection.AvailableModes,
            SelectedModeId: toolingProjection.SelectedModeId,
            ConfigOptions: toolingProjection.ConfigOptions,
            ShowConfigOptionsPanel: toolingProjection.ShowConfigOptionsPanel,
            AvailableCommands: toolingProjection.AvailableCommands,
            IsHydrating: storeState.IsHydrating,
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
    string? ChatOwnerProfileId,
    string? SettingsSelectedProfileId,
    string? ForegroundTransportProfileId,
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
    string? ConnectionInstanceId,
    long ConnectionGeneration,
    string? AgentName,
    string? AgentVersion,
    string CurrentPrompt,
    IImmutableList<ConversationMessageSnapshot> Transcript,
    TranscriptProjectionRestoreProjection RestoreProjection,
    bool ShowPlanPanel,
    string? PlanTitle,
    IReadOnlyList<ConversationPlanEntrySnapshot> PlanEntries,
    IReadOnlyList<ConversationModeOptionSnapshot> AvailableModes,
    string? SelectedModeId,
    IReadOnlyList<ConversationConfigOptionSnapshot> ConfigOptions,
    bool ShowConfigOptionsPanel,
    IReadOnlyList<ConversationAvailableCommandSnapshot> AvailableCommands,
    bool IsHydrating,
    bool IsTurnStatusVisible,
    string TurnStatusText,
    bool IsTurnStatusRunning,
    ChatTurnPhase? TurnPhase);
