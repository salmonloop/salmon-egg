using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using SalmonEgg.Presentation.Core.Mvux.ShellLayout;
using Uno.Extensions.Reactive;

namespace SalmonEgg.Presentation.Core.ViewModels.ShellLayout;

public sealed partial class ShellLayoutViewModel : ObservableObject, IDisposable
{
    private readonly IState<ShellLayoutSnapshot>? _snapshotState;
    private readonly IState<ShellLayoutState>? _desiredState;
    private readonly SynchronizationContext _syncContext;
    private readonly CancellationTokenSource _projectionCts = new();
    private IDisposable? _subscription;
    private IDisposable? _desiredSubscription;
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
    [ObservableProperty] private RightPanelMode _rightPanelMode;
    [ObservableProperty] private bool _bottomPanelVisible;
    [ObservableProperty] private double _bottomPanelHeight;
    [ObservableProperty] private BottomPanelMode _bottomPanelMode;
    [ObservableProperty] private RightPanelMode _desiredRightPanelMode;
    [ObservableProperty] private BottomPanelMode _desiredBottomPanelMode;
    [ObservableProperty] private double _leftNavResizerLeft;

    public ShellLayoutViewModel(IShellLayoutStore store, SynchronizationContext? syncContext = null)
    {
        _syncContext = syncContext ?? SynchronizationContext.Current ?? new SynchronizationContext();
        ApplySnapshot(ShellLayoutPolicy.Compute(ShellLayoutState.Default));
        ApplyDesiredState(ShellLayoutState.Default);
        _desiredState = State.FromFeed(this, store.State);
        _snapshotState = State.FromFeed(this, store.Snapshot);
        _snapshotState.ForEach(async (snapshot, ct) =>
        {
            if (snapshot is null || _disposed || _projectionCts.IsCancellationRequested || ct.IsCancellationRequested)
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

                    ApplySnapshot(snapshot);
                }).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_projectionCts.IsCancellationRequested || ct.IsCancellationRequested)
            {
                // Expected during disposal.
            }
        }, out _subscription);

        if (_desiredState is not null)
        {
            _desiredState.ForEach(async (state, ct) =>
            {
                if (state is null || _disposed || _projectionCts.IsCancellationRequested || ct.IsCancellationRequested)
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

                        ApplyDesiredState(state);
                    }).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_projectionCts.IsCancellationRequested || ct.IsCancellationRequested)
                {
                    // Expected during disposal.
                }
            }, out _desiredSubscription);
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
        RightPanelVisible = snapshot.RightPanelVisible;
        RightPanelWidth = snapshot.RightPanelWidth;
        RightPanelMode = snapshot.RightPanelMode;
        BottomPanelVisible = snapshot.BottomPanelVisible;
        BottomPanelHeight = snapshot.BottomPanelHeight;
        BottomPanelMode = snapshot.BottomPanelMode;
        IsNavResizerVisible = snapshot.IsNavResizerVisible;
        LeftNavResizerLeft = snapshot.LeftNavResizerLeft;
    }

    private void ApplyDesiredState(ShellLayoutState state)
    {
        DesiredRightPanelMode = state.DesiredRightPanelMode;
        DesiredBottomPanelMode = state.DesiredBottomPanelMode;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _projectionCts.Cancel();
        _subscription?.Dispose();
        _desiredSubscription?.Dispose();
        _projectionCts.Dispose();
    }

    private Task PostToUiAsync(Action action)
    {
        if (SynchronizationContext.Current == _syncContext)
        {
            action();
            return Task.CompletedTask;
        }

        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _syncContext.Post(_ =>
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
        }, null);

        return tcs.Task;
    }
}
