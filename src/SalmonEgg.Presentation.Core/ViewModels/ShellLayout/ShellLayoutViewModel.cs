using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using SalmonEgg.Presentation.Core.Mvux.ShellLayout;
using Uno.Extensions.Reactive;

namespace SalmonEgg.Presentation.Core.ViewModels.ShellLayout;

public sealed partial class ShellLayoutViewModel : ObservableObject, IDisposable
{
    private readonly IShellLayoutStore _store;
    private IDisposable? _subscription;

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
    [ObservableProperty] private bool _rightPanelVisible;
    [ObservableProperty] private double _rightPanelWidth;
    [ObservableProperty] private RightPanelMode _rightPanelMode;
    [ObservableProperty] private double _leftNavResizerLeft;

    public ShellLayoutViewModel(IShellLayoutStore store)
    {
        _store = store;
        _store.SnapshotState.ForEach(async (snapshot, ct) =>
        {
            if (snapshot is null) return;
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
            RightPanelVisible = snapshot.RightPanelVisible;
            RightPanelWidth = snapshot.RightPanelWidth;
            RightPanelMode = snapshot.RightPanelMode;
            IsNavResizerVisible = snapshot.IsNavResizerVisible;
            LeftNavResizerLeft = snapshot.LeftNavResizerLeft;
        }, out _subscription);
    }

    public void Dispose() => _subscription?.Dispose();
}
