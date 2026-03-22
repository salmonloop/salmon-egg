using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SalmonEgg.Presentation.Services;

namespace SalmonEgg.Presentation.ViewModels.Navigation;

public abstract partial class MainNavItemViewModel : ObservableObject, IDisposable
{
    protected readonly INavigationPaneState NavigationState;
    private bool _isLogicallySelected;

    public ObservableCollection<MainNavItemViewModel> Children { get; } = new();

    protected MainNavItemViewModel(INavigationPaneState navigationState)
    {
        NavigationState = navigationState;
        NavigationState.PaneStateChanged += OnServicePaneStateChanged;
    }

    public bool IsPaneOpen => NavigationState.IsPaneOpen;

    public bool IsPaneClosed => !IsPaneOpen;

    public bool IsLogicallySelected
    {
        get => _isLogicallySelected;
        set => SetProperty(ref _isLogicallySelected, value);
    }

    private void OnServicePaneStateChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(IsPaneOpen));
        OnPropertyChanged(nameof(IsPaneClosed));
        OnPaneStateChanged();
    }

    protected virtual void OnPaneStateChanged()
    {
    }

    public virtual void Dispose()
    {
        NavigationState.PaneStateChanged -= OnServicePaneStateChanged;
        foreach (var child in Children)
        {
            child.Dispose();
        }
        Children.Clear();
    }
}

/// <summary>
/// Label-only VM rendered as <c>NavigationViewItemHeader</c> which natively
/// collapses to zero height in compact mode.
/// </summary>
public sealed partial class SessionsLabelNavItemViewModel : MainNavItemViewModel
{
    public string Title { get; } = "会话";

    public SessionsLabelNavItemViewModel(INavigationPaneState navigationState)
        : base(navigationState)
    {
    }
}

/// <summary>
/// Action VM rendered as a standard <c>NavigationViewItem</c> with a static
/// Add icon.  In compact mode only the icon is visible (same pattern as Start).
/// </summary>
public sealed partial class AddProjectNavItemViewModel : MainNavItemViewModel
{
    public IAsyncRelayCommand AddProjectCommand { get; }

    public AddProjectNavItemViewModel(IAsyncRelayCommand addProjectCommand, INavigationPaneState navigationState)
        : base(navigationState)
    {
        AddProjectCommand = addProjectCommand;
    }
}
