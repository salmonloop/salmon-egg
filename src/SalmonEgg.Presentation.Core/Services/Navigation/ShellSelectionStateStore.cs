using System;
using CommunityToolkit.Mvvm.ComponentModel;
using SalmonEgg.Presentation.Models.Navigation;

namespace SalmonEgg.Presentation.Core.Services;

public sealed partial class ShellSelectionStateStore : ObservableObject, IShellSelectionReadModel, IShellSelectionMutationSink, IShellNavigationRuntimeState
{
    [ObservableProperty]
    private NavigationSelectionState _currentSelection = NavigationSelectionState.StartSelection;

    public long LatestActivationToken { get; set; }

    public long ActiveSessionActivationVersion { get; set; }

    public long CommittedSessionActivationVersion { get; set; }

    public string? DesiredSessionId { get; set; }

    public string? CommittedSessionId { get; set; }

    public bool IsSessionActivationInProgress { get; set; }

    public ShellNavigationContent CurrentShellContent { get; set; } = ShellNavigationContent.Start;

    public void SetSelection(NavigationSelectionState selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        CurrentSelection = selection;
    }
}
