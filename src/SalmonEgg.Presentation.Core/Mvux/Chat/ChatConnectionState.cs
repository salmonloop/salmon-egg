namespace SalmonEgg.Presentation.Core.Mvux.Chat;

public enum ConnectionPhase
{
    Disconnected = 0,
    Connecting = 1,
    Connected = 2,
    Error = 3
}

public sealed record ChatConnectionState(
    ConnectionPhase Phase,
    string? SelectedProfileId,
    string? Error,
    bool IsAuthenticationRequired,
    string? AuthenticationHintMessage,
    long Generation)
{
    public static ChatConnectionState Empty { get; } = new(
        ConnectionPhase.Disconnected,
        SelectedProfileId: null,
        Error: null,
        IsAuthenticationRequired: false,
        AuthenticationHintMessage: null,
        Generation: 0);
}
