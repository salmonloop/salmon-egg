using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SalmonEgg.Presentation.Core.Mvux.ShellLayout;
using SalmonEgg.Presentation.Core.Services;

namespace SalmonEgg.Presentation.Core.ViewModels.ShellLayout;

public sealed partial class ShellLayoutViewModel : ObservableObject, IDisposable
{
    private readonly IShellLayoutStore _store;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly CancellationTokenSource _projectionCts = new();
    private bool _disposed;

    [ObservableProperty] private NavigationPaneDisplayMode _navPaneDisplayMode;
    [ObservableProperty] private bool _isNavPaneOpen;
    [ObservableProperty] private double _navOpenPaneLength;
    [ObservableProperty] private double _navCompactPaneLength;
    [ObservableProperty] private bool _searchBoxVisible;
    [ObservableProperty] private double _searchBoxMinWidth;
    [ObservableProperty] private double _searchBoxMaxWidth;
    [ObservableProperty] private LayoutPadding _titleBarPadding;
    [ObservableProperty] private bool _isNavResizerVisible;
    [ObservableProperty] private LayoutPadding _navViewPadding;
    [ObservableProperty] private double _titleBarHeight;
    [ObservableProperty] private bool _canShowSimultaneousAuxiliaryPanels;
    [ObservableProperty] private bool _rightPanelVisible;
    [ObservableProperty] private double _rightPanelWidth;
    [ObservableProperty] private double _rightPanelOpenPaneLength;
    [ObservableProperty] private RightPanelMode _rightPanelMode;
    [ObservableProperty] private bool _bottomPanelVisible;
    [ObservableProperty] private double _bottomPanelHeight;
    [ObservableProperty] private BottomPanelMode _bottomPanelMode;
    [ObservableProperty] private bool _canToggleTaskOverviewPanel;
    [ObservableProperty] private bool _canToggleBottomPanel;
    [ObservableProperty] private bool _showAuxiliaryTitleBarButtons;
    [ObservableProperty] private bool _supportsLocalTerminal;
    [ObservableProperty] private int _titleBarInteractiveRegionToken;
    [ObservableProperty] private RightPanelMode _desiredRightPanelMode;
    [ObservableProperty] private BottomPanelMode _desiredBottomPanelMode;
    [ObservableProperty] private double _leftNavResizerLeft;

    public ShellLayoutViewModel(IShellLayoutStore store, IUiDispatcher uiDispatcher)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
        ApplySnapshot(_store.CurrentSnapshot);
        ApplyDesiredState(_store.CurrentState);
        _store.Changed += OnShellLayoutChanged;
    }

    private async void OnShellLayoutChanged(object? sender, ShellLayoutChangedEventArgs e)
    {
        if (_disposed || _projectionCts.IsCancellationRequested)
        {
            return;
        }

        try
        {
            await PostToUiAsync(() =>
            {
                if (_disposed || _projectionCts.IsCancellationRequested)
                {
                    return;
                }

                ApplySnapshot(e.Snapshot);
                ApplyDesiredState(e.State);
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_projectionCts.IsCancellationRequested)
        {
            // Expected during disposal.
        }
    }

    private void ApplySnapshot(ShellLayoutSnapshot snapshot)
    {
        NavPaneDisplayMode = snapshot.NavPaneDisplayMode;
        IsNavPaneOpen = snapshot.IsNavPaneOpen;
        NavOpenPaneLength = snapshot.NavOpenPaneLength;
        NavCompactPaneLength = snapshot.NavCompactPaneLength;
        SearchBoxVisible = snapshot.SearchBoxVisible;
        SearchBoxMinWidth = snapshot.SearchBoxMinWidth;
        SearchBoxMaxWidth = snapshot.SearchBoxMaxWidth;
        TitleBarPadding = snapshot.TitleBarPadding;
        NavViewPadding = snapshot.NavViewPadding;
        TitleBarHeight = snapshot.TitleBarHeight;
        CanShowSimultaneousAuxiliaryPanels = snapshot.CanShowSimultaneousAuxiliaryPanels;
        RightPanelOpenPaneLength = snapshot.RightPanelOpenPaneLength;
        RightPanelWidth = snapshot.RightPanelWidth;
        RightPanelVisible = snapshot.RightPanelVisible;
        RightPanelMode = snapshot.RightPanelMode;
        BottomPanelVisible = snapshot.BottomPanelVisible;
        BottomPanelHeight = snapshot.BottomPanelHeight;
        BottomPanelMode = snapshot.BottomPanelMode;
        CanToggleTaskOverviewPanel = snapshot.CanToggleTaskOverviewPanel;
        CanToggleBottomPanel = snapshot.CanToggleBottomPanel;
        ShowAuxiliaryTitleBarButtons = snapshot.ShowAuxiliaryTitleBarButtons;
        SupportsLocalTerminal = snapshot.SupportsLocalTerminal;
        TitleBarInteractiveRegionToken = snapshot.TitleBarInteractiveRegionToken;
        IsNavResizerVisible = snapshot.IsNavResizerVisible;
        LeftNavResizerLeft = snapshot.LeftNavResizerLeft;
    }

    private void ApplyDesiredState(ShellLayoutState state)
    {
        DesiredRightPanelMode = state.DesiredRightPanelMode;
        DesiredBottomPanelMode = state.DesiredBottomPanelMode;
    }

    [RelayCommand(CanExecute = nameof(CanToggleTaskOverviewPanel))]
    private async Task ToggleTaskOverviewPanelAsync()
    {
        if (!CanToggleTaskOverviewPanel)
        {
            return;
        }

        await _store.Dispatch(new ToggleRightPanelRequested(RightPanelMode.TaskOverview));
    }

    [RelayCommand(CanExecute = nameof(CanToggleBottomPanel))]
    private async Task ToggleBottomPanelAsync()
    {
        if (!CanToggleBottomPanel)
        {
            return;
        }

        await _store.Dispatch(new ToggleBottomPanelRequested());
    }

    partial void OnCanToggleTaskOverviewPanelChanged(bool value) => ToggleTaskOverviewPanelCommand.NotifyCanExecuteChanged();
    partial void OnCanToggleBottomPanelChanged(bool value) => ToggleBottomPanelCommand.NotifyCanExecuteChanged();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _projectionCts.Cancel();
        _store.Changed -= OnShellLayoutChanged;
        _projectionCts.Dispose();
    }

    private Task PostToUiAsync(Action action)
    {
        if (_uiDispatcher.HasThreadAccess)
        {
            action();
            return Task.CompletedTask;
        }

        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _uiDispatcher.Enqueue(() =>
        {
            try
            {
                action();
                tcs.TrySetResult(null);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });

        return tcs.Task;
    }
}
