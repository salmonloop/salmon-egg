using SalmonEgg.Presentation.Core.Mvux.Chat;

namespace SalmonEgg.Presentation.Core.Services.Chat;

public enum ChatTurnStatusSource
{
    None,
    ClientLifecycle,
    AcpSessionUpdate,
    PromptResult
}

internal readonly record struct ChatTurnStatusPresentation(
    bool IsVisible,
    ChatTurnStatusSource Source,
    string ResourceKey,
    string FallbackText,
    string? FormatArgument,
    bool IsRunning,
    bool IsPromptSubmitInFlight,
    bool IsFaulted);

internal static class ChatTurnStatusPresentationPolicy
{
    public static ChatTurnStatusPresentation Resolve(ActiveTurnState? turn)
    {
        if (turn is null)
        {
            return Hidden();
        }

        return turn.Phase switch
        {
            ChatTurnPhase.CreatingRemoteSession => Visible(
                ChatTurnStatusSource.ClientLifecycle,
                "ChatTurnStatus_CreatingRemoteSession",
                "Starting session...",
                isRunning: true,
                isPromptSubmitInFlight: true),

            ChatTurnPhase.DispatchingPrompt => Visible(
                ChatTurnStatusSource.ClientLifecycle,
                "ChatTurnStatus_DispatchingPrompt",
                "Sending prompt...",
                isRunning: true,
                isPromptSubmitInFlight: true),

            ChatTurnPhase.WaitingForAgent => Visible(
                ChatTurnStatusSource.ClientLifecycle,
                "ChatTurnStatus_WaitingForAgent",
                "Waiting for agent...",
                isRunning: true),

            ChatTurnPhase.Thinking => Visible(
                ChatTurnStatusSource.AcpSessionUpdate,
                "ChatTurnStatus_Thinking",
                "Thinking...",
                isRunning: true),

            ChatTurnPhase.ToolPending => Visible(
                ChatTurnStatusSource.AcpSessionUpdate,
                "ChatTurnStatus_ToolPending",
                "Preparing tool call...",
                isRunning: true),

            ChatTurnPhase.ToolRunning => Visible(
                ChatTurnStatusSource.AcpSessionUpdate,
                "ChatTurnStatus_ToolRunning",
                "Running tool: {0}",
                isRunning: true,
                formatArgument: turn.ToolTitle ?? "..."),

            ChatTurnPhase.Responding => Visible(
                ChatTurnStatusSource.AcpSessionUpdate,
                "ChatTurnStatus_Responding",
                "Responding...",
                isRunning: true),

            ChatTurnPhase.Failed => Visible(
                ChatTurnStatusSource.PromptResult,
                "ChatTurnStatus_Failed",
                "Failed",
                isFaulted: true),

            ChatTurnPhase.Cancelled => Visible(
                ChatTurnStatusSource.PromptResult,
                "ChatTurnStatus_Cancelled",
                "Cancelled"),

            _ => Hidden()
        };
    }

    private static ChatTurnStatusPresentation Visible(
        ChatTurnStatusSource source,
        string resourceKey,
        string fallbackText,
        bool isRunning = false,
        bool isPromptSubmitInFlight = false,
        bool isFaulted = false,
        string? formatArgument = null)
        => new(
            IsVisible: true,
            Source: source,
            ResourceKey: resourceKey,
            FallbackText: fallbackText,
            FormatArgument: formatArgument,
            IsRunning: isRunning,
            IsPromptSubmitInFlight: isPromptSubmitInFlight,
            IsFaulted: isFaulted);

    private static ChatTurnStatusPresentation Hidden()
        => new(
            IsVisible: false,
            Source: ChatTurnStatusSource.None,
            ResourceKey: string.Empty,
            FallbackText: string.Empty,
            FormatArgument: null,
            IsRunning: false,
            IsPromptSubmitInFlight: false,
            IsFaulted: false);
}
