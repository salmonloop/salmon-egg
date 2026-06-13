using System;
using System.Collections.Immutable;
using System.Globalization;
using Microsoft.Extensions.Localization;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Resources;

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
    private readonly IStringLocalizer<CoreStrings>? _localizer;

    public ChatStateProjector()
        : this(new ChatSessionToolingProjector(), new TranscriptProjectionRestoreTokenProjector(), localizer: null)
    {
    }

    public ChatStateProjector(IStringLocalizer<CoreStrings> localizer)
        : this(new ChatSessionToolingProjector(), new TranscriptProjectionRestoreTokenProjector(), localizer)
    {
    }

    public ChatStateProjector(
        ChatSessionToolingProjector sessionToolingProjector,
        TranscriptProjectionRestoreTokenProjector restoreTokenProjector,
        IStringLocalizer<CoreStrings>? localizer = null)
    {
        _sessionToolingProjector = sessionToolingProjector ?? throw new ArgumentNullException(nameof(sessionToolingProjector));
        _restoreTokenProjector = restoreTokenProjector ?? throw new ArgumentNullException(nameof(restoreTokenProjector));
        _localizer = localizer;
    }

    public ChatUiProjection Apply(
        ChatState storeState,
        ChatConnectionState connectionState,
        string? hydratedConversationId,
        ConversationRemoteBindingState? binding)
    {
        ArgumentNullException.ThrowIfNull(storeState);
        var selectedProfileIntentId = connectionState.SelectedProfileIntentId;
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
        var connectionStatus = connectionState.Phase switch
        {
            ConnectionPhase.Connected => Localize("ChatConnectionStatus_Connected", "Connected"),
            ConnectionPhase.Initializing => Localize("ChatConnectionStatus_Initializing", "Initializing..."),
            ConnectionPhase.Connecting => Localize("ChatConnectionStatus_Connecting", "Connecting..."),
            _ => Localize("ChatConnectionStatus_Disconnected", "Disconnected")
        };
        var connectionError = connectionState.Error;
        var isAuthenticationRequired = connectionState.IsAuthenticationRequired;
        var authenticationHintMessage = connectionState.AuthenticationHintMessage;

        var activeTurn = GetVisibleActiveTurn(storeState.ActiveTurn, hydratedConversationId);
        var isTurnStatusVisible = activeTurn is not null && activeTurn.Phase != ChatTurnPhase.Completed;
        var turnStatusText = GetTurnStatusText(activeTurn);
        var isTurnStatusRunning = IsRunningTurn(activeTurn);
        var isPromptSubmitInFlight = IsPromptSubmitInFlight(activeTurn);
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
            firstVisibleIndex: transcript.Count > 0 ? transcript.Count - 1 : -1);

        return new ChatUiProjection(
            HydratedConversationId: hydratedConversationId,
            ChatOwnerProfileId: chatOwnerProfileId,
            SelectedProfileIntentId: selectedProfileIntentId,
            ForegroundTransportProfileId: foregroundTransportProfileId,
            RemoteSessionId: binding?.RemoteSessionId,
            IsSessionActive: !string.IsNullOrWhiteSpace(hydratedConversationId),
            IsPromptInFlight: isTurnStatusRunning,
            IsPromptSubmitInFlight: isPromptSubmitInFlight,
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

    private string GetTurnStatusText(ActiveTurnState? turn)
    {
        if (turn == null) return string.Empty;
        return turn.Phase switch
        {
            ChatTurnPhase.CreatingRemoteSession => Localize("ChatTurnStatus_CreatingRemoteSession", "Starting session..."),
            ChatTurnPhase.DispatchingPrompt => Localize("ChatTurnStatus_DispatchingPrompt", "Sending prompt..."),
            ChatTurnPhase.WaitingForAgent => Localize("ChatTurnStatus_WaitingForAgent", "Waiting for agent..."),
            ChatTurnPhase.Thinking => Localize("ChatTurnStatus_Thinking", "Thinking..."),
            ChatTurnPhase.ToolPending => Localize("ChatTurnStatus_ToolPending", "Preparing tool call..."),
            ChatTurnPhase.ToolRunning => FormatLocalize("ChatTurnStatus_ToolRunning", "Running tool: {0}", turn.ToolTitle ?? "..."),
            ChatTurnPhase.Responding => Localize("ChatTurnStatus_Responding", "Responding..."),
            ChatTurnPhase.Completed => Localize("ChatTurnStatus_Completed", "Completed"),
            ChatTurnPhase.Failed => FormatLocalize("ChatTurnStatus_Failed", "Failed: {0}", turn.FailureMessage ?? Localize("ChatTurnStatus_UnknownError", "Unknown error")),
            ChatTurnPhase.Cancelled => Localize("ChatTurnStatus_Cancelled", "Cancelled"),
            _ => string.Empty
        };
    }

    private string Localize(string key, string fallback)
    {
        if (_localizer is null)
        {
            return fallback;
        }

        var localized = _localizer[key];
        return localized.ResourceNotFound ? fallback : localized.Value;
    }

    private string FormatLocalize(string key, string fallback, params object[] arguments)
    {
        if (_localizer is null)
        {
            return string.Format(CultureInfo.CurrentCulture, fallback, arguments);
        }

        var localized = _localizer[key, arguments];
        return localized.ResourceNotFound
            ? string.Format(CultureInfo.CurrentCulture, fallback, arguments)
            : localized.Value;
    }

    private static bool IsRunningTurn(ActiveTurnState? turn)
        => turn is not null
            && turn.Phase is not ChatTurnPhase.Completed
            && turn.Phase is not ChatTurnPhase.Failed
            && turn.Phase is not ChatTurnPhase.Cancelled;

    private static bool IsPromptSubmitInFlight(ActiveTurnState? turn)
        => turn?.Phase is ChatTurnPhase.CreatingRemoteSession or ChatTurnPhase.DispatchingPrompt;
}

public sealed record ChatUiProjection(
    string? HydratedConversationId,
    string? ChatOwnerProfileId,
    string? SelectedProfileIntentId,
    string? ForegroundTransportProfileId,
    string? RemoteSessionId,
    bool IsSessionActive,
    bool IsPromptInFlight,
    bool IsPromptSubmitInFlight,
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
