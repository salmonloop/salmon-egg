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

    public ObservableCollection<MainNavItemViewModel> Children { get; } = new();

    protected MainNavItemViewModel(INavigationPaneState navigationState, IUiDispatcher uiDispatcher)
    {
        NavigationState = navigationState;
        _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
        NavigationState.PaneStateChanged += OnServicePaneStateChanged;
    }

    public bool IsPaneOpen => NavigationState.IsPaneOpen;

    public bool IsPaneClosed => !IsPaneOpen;

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
    private string _title = "Sessions";

    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    public SessionsLabelNavItemViewModel(INavigationPaneState navigationState, IUiDispatcher uiDispatcher, string title = "Sessions")
        : base(navigationState, uiDispatcher)
    {
        Title = title;
    }

    public void UpdateTitle(string title)
    {
        Title = title;
    }
}

/// <summary>
/// Action VM rendered as a standard <c>NavigationViewItem</c> with a static
/// Add icon.  In compact mode only the icon is visible (same pattern as Start).
/// Invoking the item opens an attached menu offering the local-folder and remote
/// source intents; both funnel through the unified add-project coordinator.
/// </summary>
public sealed partial class AddProjectNavItemViewModel : MainNavItemViewModel
{
    public IAsyncRelayCommand AddLocalProjectCommand { get; }

    public IAsyncRelayCommand SelectRemoteProjectCommand { get; }

    public AddProjectNavItemViewModel(
        IAsyncRelayCommand addLocalProjectCommand,
        IAsyncRelayCommand selectRemoteProjectCommand,
        INavigationPaneState navigationState,
        IUiDispatcher uiDispatcher)
        : base(navigationState, uiDispatcher)
    {
        AddLocalProjectCommand = addLocalProjectCommand ?? throw new ArgumentNullException(nameof(addLocalProjectCommand));
        SelectRemoteProjectCommand = selectRemoteProjectCommand ?? throw new ArgumentNullException(nameof(selectRemoteProjectCommand));
    }
}
