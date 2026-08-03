using System;
using SalmonEgg.Presentation.Models.Navigation;

namespace SalmonEgg.Presentation.Core.Services;

public static class NavigationSelectionProjectionPolicy
{
    public static NavigationSelectionState ResolveProjectedSelection(
        NavigationSelectionState committedSelection,
        SessionActivationSnapshot? activeActivation,
        ShellNavigationContent? pendingShellContent,
        long latestActivationToken,
        Predicate<string> canProjectSession)
    {
        ArgumentNullException.ThrowIfNull(committedSelection);
        ArgumentNullException.ThrowIfNull(canProjectSession);

        var activeSessionId = ResolveActiveSessionActivationProjectionSessionId(
            activeActivation,
            pendingShellContent,
            latestActivationToken);
        if (!string.IsNullOrWhiteSpace(activeSessionId) && canProjectSession(activeSessionId))
        {
            return new NavigationSelectionState.Session(activeSessionId);
        }

        return committedSelection;
    }

    public static string? ResolveSelectionSessionId(NavigationSelectionState selection)
    {
        ArgumentNullException.ThrowIfNull(selection);

        return selection is NavigationSelectionState.Session { SessionId: { } sessionId }
               && !string.IsNullOrWhiteSpace(sessionId)
            ? sessionId
            : null;
    }

    public static string? ResolveActiveSessionActivationProjectionSessionId(
        SessionActivationSnapshot? activeActivation,
        ShellNavigationContent? pendingShellContent,
        long latestActivationToken)
    {
        if (activeActivation is null
            || string.IsNullOrWhiteSpace(activeActivation.SessionId)
            || IsTerminalSessionActivationPhase(activeActivation.Phase))
        {
            return null;
        }

        if (pendingShellContent is { } pendingContent
            && pendingContent != ShellNavigationContent.Chat)
        {
            return null;
        }

        if (latestActivationToken > 0 && activeActivation.Version != latestActivationToken)
        {
            return null;
        }

        return activeActivation.SessionId;
    }

    private static bool IsTerminalSessionActivationPhase(SessionActivationPhase phase)
        => phase is SessionActivationPhase.None or SessionActivationPhase.Hydrated or SessionActivationPhase.Faulted;
}
