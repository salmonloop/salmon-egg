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
}
