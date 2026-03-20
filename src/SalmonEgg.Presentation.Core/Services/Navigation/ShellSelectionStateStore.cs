using System;
using CommunityToolkit.Mvvm.ComponentModel;
using SalmonEgg.Presentation.Models.Navigation;

namespace SalmonEgg.Presentation.Core.Services;

public sealed partial class ShellSelectionStateStore : ObservableObject, IShellSelectionReadModel, IShellSelectionMutationSink
{
    [ObservableProperty]
    private NavigationSelectionState _currentSelection = NavigationSelectionState.StartSelection;

    public void SetSelection(NavigationSelectionState selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        CurrentSelection = selection;
    }
}
