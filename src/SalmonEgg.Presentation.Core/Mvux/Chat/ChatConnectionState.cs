using System.Collections.Immutable;
using SalmonEgg.Domain.Models.Conversation;

namespace SalmonEgg.Presentation.Core.Mvux.Chat;

public enum ConnectionPhase
{
    Disconnected = 0,
    Connecting = 1,
    Initializing = 2,
    Connected = 3,
    Error = 4
}

public enum NewSessionDraftPhase
{
    Creating = 0,
    Ready = 1,
    Faulted = 2,
    Promoting = 3,
    Closing = 4
}

public sealed record NewSessionDraftState(
    string ProfileId,
    string Cwd,
    string? RemoteSessionId,
    string ConnectionInstanceId,
    NewSessionDraftPhase Phase,
    long Version,
    IImmutableList<ConversationModeOptionSnapshot> AvailableModes,
    string? SelectedModeId,
    IImmutableList<ConversationConfigOptionSnapshot> ConfigOptions,
    bool ShowConfigOptionsPanel,
    IImmutableList<ConversationAvailableCommandSnapshot> AvailableCommands,
    ConversationSessionInfoSnapshot? SessionInfo,
    bool IsConfigAuthoritative = false,
    string? Error = null);

public sealed record ChatConnectionState(
    ConnectionPhase Phase,
    string? SettingsSelectedProfileId,
    string? Error,
    bool IsAuthenticationRequired,
    string? AuthenticationHintMessage,
    long Generation,
    string? ConnectionInstanceId = null,
    string? ForegroundTransportProfileId = null,
    NewSessionDraftState? NewSessionDraft = null)
{
    public static ChatConnectionState Empty { get; } = new(
        ConnectionPhase.Disconnected,
        SettingsSelectedProfileId: null,
        Error: null,
        IsAuthenticationRequired: false,
        AuthenticationHintMessage: null,
        Generation: 0,
        ConnectionInstanceId: null,
        ForegroundTransportProfileId: null,
        NewSessionDraft: null);
}
