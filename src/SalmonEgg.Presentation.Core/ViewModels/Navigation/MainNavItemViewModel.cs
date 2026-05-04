using System;
using System.Collections.ObjectModel;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.Core.Services;

namespace SalmonEgg.Presentation.ViewModels.Navigation;

public abstract partial class MainNavItemViewModel : ObservableObject, IDisposable
{
    protected readonly INavigationPaneState NavigationState;
    private readonly IUiDispatcher _uiDispatcher;
    private bool _isLogicallySelected;

    public ObservableCollection<MainNavItemViewModel> Children { get; } = new();

    protected MainNavItemViewModel(INavigationPaneState navigationState, IUiDispatcher uiDispatcher)
    {
        NavigationState = navigationState;
        _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
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
        _uiDispatcher.Enqueue(ApplyPaneStateChanged);
    }

    private void ApplyPaneStateChanged()
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
    public string Title { get; }

    public SessionsLabelNavItemViewModel(INavigationPaneState navigationState, IUiDispatcher uiDispatcher, string title = "会话")
        : base(navigationState, uiDispatcher)
    {
        Title = title;
    }
}

/// <summary>
/// Action VM rendered as a standard <c>NavigationViewItem</c> with a static
/// Add icon.  In compact mode only the icon is visible (same pattern as Start).
/// </summary>
public sealed partial class AddProjectNavItemViewModel : MainNavItemViewModel
{
    public IAsyncRelayCommand AddProjectCommand { get; }

    public AddProjectNavItemViewModel(IAsyncRelayCommand addProjectCommand, INavigationPaneState navigationState, IUiDispatcher uiDispatcher)
        : base(navigationState, uiDispatcher)
    {
        AddProjectCommand = addProjectCommand;
    }
}
