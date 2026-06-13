namespace SalmonEgg.Presentation.Core.Mvux.Chat;

public abstract record ChatConnectionAction;

public sealed record SetConnectionPhaseAction(
    ConnectionPhase Phase,
    string? Error = null) : ChatConnectionAction;

public sealed record SetSelectedProfileIntentAction(string? ProfileId) : ChatConnectionAction;

public sealed record SetForegroundTransportProfileAction(string? ProfileId) : ChatConnectionAction;

public sealed record SetConnectionInstanceIdAction(string? ConnectionInstanceId) : ChatConnectionAction;

public sealed record SetConnectionAuthenticationStateAction(
    bool IsRequired,
    string? HintMessage) : ChatConnectionAction;

public sealed record SetNewSessionDraftAction(NewSessionDraftState? Draft) : ChatConnectionAction;

public sealed record ClearNewSessionDraftAction : ChatConnectionAction;

public sealed record ResetConnectionStateAction : ChatConnectionAction;
