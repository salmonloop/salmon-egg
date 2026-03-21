namespace SalmonEgg.Presentation.Core.Mvux.Chat;

public abstract record ChatConnectionAction;

public sealed record SetConnectionPhaseAction(
    ConnectionPhase Phase,
    string? Error = null) : ChatConnectionAction;

public sealed record SetSelectedProfileAction(string? ProfileId) : ChatConnectionAction;

public sealed record SetConnectionAuthenticationStateAction(
    bool IsRequired,
    string? HintMessage) : ChatConnectionAction;

public sealed record ResetConnectionStateAction : ChatConnectionAction;
