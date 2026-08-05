using CommunityToolkit.Mvvm.ComponentModel;
using SalmonEgg.Presentation.Models.Navigation;

namespace SalmonEgg.Presentation.Core.Services;

public sealed partial class ShellNavigationRuntimeStateStore : ObservableObject, IShellNavigationRuntimeState
{
    public long LatestActivationToken { get; set; }

    [ObservableProperty]
    private SessionActivationSnapshot? _activeSessionActivation;

    public long ActiveSessionActivationVersion { get; set; }

    public long CommittedSessionActivationVersion { get; set; }

    [ObservableProperty]
    private string? _desiredSessionId;

    [ObservableProperty]
    private string? _committedSessionId;

    [ObservableProperty]
    private bool _isSessionActivationInProgress;

    [ObservableProperty]
    private ShellNavigationContent _currentShellContent = ShellNavigationContent.Start;

    [ObservableProperty]
    private ShellNavigationContent? _pendingShellContent;

    public bool TryRetireSession(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return false;
        }

        var ownsActivation =
            ActiveSessionActivation?.Matches(sessionId) == true
            || string.Equals(DesiredSessionId, sessionId, StringComparison.Ordinal)
            || string.Equals(CommittedSessionId, sessionId, StringComparison.Ordinal);
        if (!ownsActivation)
        {
            return false;
        }

        if (ActiveSessionActivation?.Matches(sessionId) == true)
        {
            ActiveSessionActivation = null;
        }

        if (string.Equals(DesiredSessionId, sessionId, StringComparison.Ordinal))
        {
            DesiredSessionId = null;
        }

        // The committed id and its version describe one fact, so they retire together.
        if (string.Equals(CommittedSessionId, sessionId, StringComparison.Ordinal))
        {
            CommittedSessionId = null;
            CommittedSessionActivationVersion = 0;
        }

        IsSessionActivationInProgress = false;
        ActiveSessionActivationVersion = 0;
        return true;
    }
}
