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

        var ownsInFlightActivation = ActiveSessionActivation?.Matches(sessionId) == true;
        var ownsActivation =
            ownsInFlightActivation
            || string.Equals(DesiredSessionId, sessionId, StringComparison.Ordinal)
            || string.Equals(CommittedSessionId, sessionId, StringComparison.Ordinal);
        if (!ownsActivation)
        {
            return false;
        }

        if (ownsInFlightActivation)
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

        // The progress flags describe ActiveSessionActivation, so retire them with it. When only the
        // committed or desired id matched, a different conversation owns the in-flight activation and
        // its progress must survive: clearing it would leave that activation running behind a runtime
        // that reports nothing in progress.
        if (ownsInFlightActivation)
        {
            IsSessionActivationInProgress = false;
            ActiveSessionActivationVersion = 0;
        }

        return true;
    }
}
