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
        var turnStatus = ChatTurnStatusPresentationPolicy.Resolve(activeTurn);
        var turnStatusText = FormatTurnStatusText(turnStatus);
        var turnFailureMessage = ResolveTurnFailureMessage(activeTurn);
        var isTurnFailureVisible = !string.IsNullOrWhiteSpace(turnFailureMessage);
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
            IsPromptInFlight: turnStatus.IsRunning,
            IsPromptSubmitInFlight: turnStatus.IsPromptSubmitInFlight,
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
            DraftRevision: storeState.DraftRevision,
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
            IsTurnStatusVisible: turnStatus.IsVisible,
            TurnStatusText: turnStatusText,
            IsTurnStatusRunning: turnStatus.IsRunning,
            IsTurnStatusFaulted: turnStatus.IsFaulted,
            TurnStatusSource: turnStatus.Source,
            TurnPhase: activeTurn?.Phase,
            IsTurnFailureVisible: isTurnFailureVisible,
            TurnFailureTitle: isTurnFailureVisible
                ? Localize("ChatTurnFailure_Title", "Turn failed")
                : string.Empty,
            TurnFailureMessage: turnFailureMessage,
            TurnFailureCopyActionText: isTurnFailureVisible
                ? Localize("ChatTurnFailure_CopyAction", "Copy failure detail")
                : string.Empty,
            TurnFailureDismissActionText: isTurnFailureVisible
                ? Localize("ChatTurnFailure_DismissAction", "Dismiss failure detail")
                : string.Empty);
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

    private string FormatTurnStatusText(ChatTurnStatusPresentation presentation)
    {
        if (!presentation.IsVisible || string.IsNullOrWhiteSpace(presentation.ResourceKey))
        {
            return string.Empty;
        }

        return presentation.FormatArgument is null
            ? Localize(presentation.ResourceKey, presentation.FallbackText)
            : FormatLocalize(presentation.ResourceKey, presentation.FallbackText, presentation.FormatArgument);
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

    private static string ResolveTurnFailureMessage(ActiveTurnState? turn)
        => turn?.Phase is ChatTurnPhase.Failed && !string.IsNullOrWhiteSpace(turn.FailureMessage)
            ? turn.FailureMessage
            : string.Empty;
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
    long DraftRevision,
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
    bool IsTurnStatusFaulted,
    ChatTurnStatusSource TurnStatusSource,
    ChatTurnPhase? TurnPhase,
    bool IsTurnFailureVisible,
    string TurnFailureTitle,
    string TurnFailureMessage,
    string TurnFailureCopyActionText,
    string TurnFailureDismissActionText);
