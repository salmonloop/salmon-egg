using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SalmonEgg.Presentation.Services;

namespace SalmonEgg.Presentation.ViewModels.Navigation;

public abstract partial class MainNavItemViewModel : ObservableObject, IDisposable
{
    protected readonly INavigationStateService NavigationState;

    public ObservableCollection<MainNavItemViewModel> Children { get; } = new();

    protected MainNavItemViewModel(INavigationStateService navigationState)
    {
        NavigationState = navigationState;
        NavigationState.PaneStateChanged += OnServicePaneStateChanged;
    }

    public bool IsPaneOpen => NavigationState.IsPaneOpen;

    public bool IsPaneClosed => !IsPaneOpen;

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

public sealed partial class SessionsHeaderNavItemViewModel : MainNavItemViewModel
{
    public string Title { get; } = "会话";

    public IAsyncRelayCommand AddProjectCommand { get; }

    public bool ShowHeaderLabel => IsPaneOpen;

    public bool ShowCompactButton => IsPaneClosed;

    public SessionsHeaderNavItemViewModel(IAsyncRelayCommand addProjectCommand, INavigationStateService navigationState)
        : base(navigationState)
    {
        AddProjectCommand = addProjectCommand;
    }

    protected override void OnPaneStateChanged()
    {
        OnPropertyChanged(nameof(ShowHeaderLabel));
        OnPropertyChanged(nameof(ShowCompactButton));
    }
}
