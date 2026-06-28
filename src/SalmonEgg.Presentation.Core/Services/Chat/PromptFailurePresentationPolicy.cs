using SalmonEgg.Presentation.Core.Mvux.Chat;

namespace SalmonEgg.Presentation.Core.Services.Chat;

internal enum PromptFailurePrimarySurface
{
    Notification,
    Transcript
}

internal readonly record struct PromptFailurePresentationDecision(
    PromptFailurePrimarySurface PrimarySurface)
{
    public bool ShouldShowNotification => PrimarySurface is PromptFailurePrimarySurface.Notification;

    public bool ShouldProjectTranscriptDetail => PrimarySurface is PromptFailurePrimarySurface.Transcript;
}

internal static class PromptFailurePresentationPolicy
{
    public static PromptFailurePresentationDecision Resolve(
        string conversationId,
        string turnId,
        ActiveTurnState? activeTurn)
    {
        if (MatchesTurn(conversationId, turnId, activeTurn)
            && HasReachedTranscriptPrimaryPhase(activeTurn!.Phase))
        {
            return new PromptFailurePresentationDecision(PromptFailurePrimarySurface.Transcript);
        }

        return new PromptFailurePresentationDecision(PromptFailurePrimarySurface.Notification);
    }

    private static bool MatchesTurn(
        string conversationId,
        string turnId,
        ActiveTurnState? activeTurn)
        => activeTurn is not null
           && string.Equals(activeTurn.ConversationId, conversationId, StringComparison.Ordinal)
           && string.Equals(activeTurn.TurnId, turnId, StringComparison.Ordinal);

    private static bool HasReachedTranscriptPrimaryPhase(ChatTurnPhase phase)
        => phase is ChatTurnPhase.WaitingForAgent
            or ChatTurnPhase.Thinking
            or ChatTurnPhase.ToolPending
            or ChatTurnPhase.ToolRunning
            or ChatTurnPhase.Responding;
}
