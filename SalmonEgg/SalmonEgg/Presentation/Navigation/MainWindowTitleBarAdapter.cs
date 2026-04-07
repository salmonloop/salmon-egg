using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
#if WINDOWS
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Windows.Foundation;
using Windows.Graphics;
#endif

namespace SalmonEgg.Presentation.Navigation;

/// <summary>
/// UI adapter that encapsulates WinUI title bar hosting and non-client passthrough region updates.
/// MainPage should only forward lifecycle events and never own title-bar stateful plumbing.
/// </summary>
public sealed class MainWindowTitleBarAdapter : IDisposable
{
    private readonly Border _appTitleBar;
    private readonly Grid _appTitleBarLayoutRoot;
    private readonly Grid _appTitleBarContent;
    private readonly FrameworkElement _titleBarDragRegion;
    private readonly FrameworkElement _titleBarLeftButtons;
    private readonly FrameworkElement _titleBarSearchBox;
    private readonly FrameworkElement _titleBarRightButtons;
    private readonly Button _titleBarBackButton;
    private readonly Frame _contentFrame;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly ILogger _logger;

#if WINDOWS
    private AppWindowTitleBar? _appWindowTitleBar;
    private Microsoft.UI.Xaml.Controls.TitleBar? _winuiTitleBarControl;
    private InputNonClientPointerSource? _titleBarPointerSource;
    private XamlRoot? _observedTitleBarXamlRoot;
#endif

#if WINDOWS
    public AppWindowTitleBar? AppWindowTitleBar => _appWindowTitleBar;
#else
    public object? AppWindowTitleBar => null;
#endif

    public MainWindowTitleBarAdapter(
        Border appTitleBar,
        Grid appTitleBarLayoutRoot,
        Grid appTitleBarContent,
        FrameworkElement titleBarDragRegion,
        FrameworkElement titleBarLeftButtons,
        FrameworkElement titleBarSearchBox,
        FrameworkElement titleBarRightButtons,
        Button titleBarBackButton,
        Frame contentFrame,
        DispatcherQueue dispatcherQueue,
        ILogger logger)
    {
        _appTitleBar = appTitleBar ?? throw new ArgumentNullException(nameof(appTitleBar));
        _appTitleBarLayoutRoot = appTitleBarLayoutRoot ?? throw new ArgumentNullException(nameof(appTitleBarLayoutRoot));
        _appTitleBarContent = appTitleBarContent ?? throw new ArgumentNullException(nameof(appTitleBarContent));
        _titleBarDragRegion = titleBarDragRegion ?? throw new ArgumentNullException(nameof(titleBarDragRegion));
        _titleBarLeftButtons = titleBarLeftButtons ?? throw new ArgumentNullException(nameof(titleBarLeftButtons));
        _titleBarSearchBox = titleBarSearchBox ?? throw new ArgumentNullException(nameof(titleBarSearchBox));
        _titleBarRightButtons = titleBarRightButtons ?? throw new ArgumentNullException(nameof(titleBarRightButtons));
        _titleBarBackButton = titleBarBackButton ?? throw new ArgumentNullException(nameof(titleBarBackButton));
        _contentFrame = contentFrame ?? throw new ArgumentNullException(nameof(contentFrame));
        _dispatcherQueue = dispatcherQueue ?? throw new ArgumentNullException(nameof(dispatcherQueue));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void Configure(Window? window)
    {
#if WINDOWS
        if (window is null)
        {
            DebugBootLog("TitleBarDiag ConfigureTitleBar skipped: window missing");
            return;
        }

        DebugBootLog("TitleBarDiag ConfigureTitleBar enter");
        AttachTitleBarXamlRootChanged();
        if (!AppWindowTitleBar.IsCustomizationSupported() || _appTitleBar.XamlRoot is null)
        {
            DebugBootLog("TitleBarDiag ConfigureTitleBar skipped: customization unsupported or XamlRoot null");
            return;
        }

        EnsureWinUiTitleBarControl();
        var titleBarElement = (UIElement?)_winuiTitleBarControl ?? _appTitleBar;

        try
        {
            window.ExtendsContentIntoTitleBar = true;
            // Prefer the WinUI TitleBar control path. If creation failed, fallback to legacy host.
            window.SetTitleBar(titleBarElement);
        }
        catch
        {
            DebugBootLog("TitleBarDiag ConfigureTitleBar skipped: SetTitleBar threw");
            return;
        }

        _titleBarLeftButtons.Visibility = Visibility.Visible;
        _titleBarBackButton.IsEnabled = _contentFrame.CanGoBack;

        var appWindow = window.AppWindow;
        if (appWindow?.TitleBar is null)
        {
            DebugBootLog("TitleBarDiag ConfigureTitleBar skipped: AppWindow.TitleBar null");
            return;
        }

        _appWindowTitleBar = appWindow.TitleBar;
        _appWindowTitleBar.ExtendsContentIntoTitleBar = true;
        _appWindowTitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        _appWindowTitleBar.BackgroundColor = Colors.Transparent;
        _appWindowTitleBar.InactiveBackgroundColor = Colors.Transparent;
        // Keep normal caption buttons transparent so they blend with our title bar,
        // but preserve system hover/pressed visuals (including the Close button red state).
        _appWindowTitleBar.ButtonBackgroundColor = Colors.Transparent;
        _appWindowTitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        _titleBarPointerSource = _winuiTitleBarControl is null
            ? InputNonClientPointerSource.GetForWindowId(window.AppWindow.Id)
            : null;
        RefreshInteractiveRegions();
        LogRightMetrics("ConfigureTitleBar");
        _ = _dispatcherQueue.TryEnqueue(() => LogRightMetrics("ConfigureTitleBar.Deferred"));
        DebugBootLog("TitleBarDiag ConfigureTitleBar complete");
#endif
    }

    public void OnHostLoaded(Window? window)
    {
#if WINDOWS
        DebugBootLog("TitleBarDiag OnAppTitleBarLoaded");
        AttachTitleBarXamlRootChanged();
        if (_appWindowTitleBar is null)
        {
            Configure(window);
        }

        RefreshInteractiveRegions();
#endif
    }

    public void OnHostSizeChanged()
    {
#if WINDOWS
        RefreshInteractiveRegions();
        LogRightMetrics("OnAppTitleBarSizeChanged");
#endif
    }

    public void OnInteractiveRegionTokenChanged()
    {
#if WINDOWS
        RefreshInteractiveRegions();
#endif
    }

    public void Detach()
    {
#if WINDOWS
        DetachTitleBarXamlRootChanged();
#endif
    }

    public void Dispose()
    {
        Detach();
    }

#if WINDOWS
    private void EnsureWinUiTitleBarControl()
    {
        if (_winuiTitleBarControl is not null)
        {
            return;
        }

        if (!ReferenceEquals(_appTitleBar.Child, _appTitleBarLayoutRoot))
        {
            return;
        }

        DetachElementFromVisualParent(_titleBarLeftButtons);
        DetachElementFromVisualParent(_titleBarSearchBox);
        DetachElementFromVisualParent(_titleBarRightButtons);

        _appTitleBarContent.Visibility = Visibility.Collapsed;
        _titleBarDragRegion.Visibility = Visibility.Collapsed;

        _appTitleBar.Child = null;
        _winuiTitleBarControl = new Microsoft.UI.Xaml.Controls.TitleBar
        {
            Background = new SolidColorBrush(Colors.Transparent),
            IsBackButtonVisible = false,
            IsPaneToggleButtonVisible = false,
            LeftHeader = _titleBarLeftButtons,
            Content = _titleBarSearchBox,
            RightHeader = _titleBarRightButtons,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        _appTitleBar.Child = _winuiTitleBarControl;
    }

    private static void DetachElementFromVisualParent(FrameworkElement element)
    {
        if (element.Parent is Panel panel)
        {
            panel.Children.Remove(element);
        }
    }

    private void LogRightMetrics(string source)
    {
#if DEBUG
        if (_appWindowTitleBar is null)
        {
            App.BootLog("TitleBarMetricsSkipped Source=" + source + " Reason=AppWindowTitleBarNull");
            return;
        }

        if (_appTitleBar.ActualWidth <= 0 || _titleBarRightButtons.ActualWidth <= 0)
        {
            App.BootLog(
                "TitleBarMetricsSkipped "
                + "Source=" + source
                + " Reason=ZeroWidth"
                + " AppTitleBarWidth=" + _appTitleBar.ActualWidth
                + " RightButtonsWidth=" + _titleBarRightButtons.ActualWidth);
            return;
        }

        try
        {
            var transform = _titleBarRightButtons.TransformToVisual(_appTitleBar);
            var origin = transform.TransformPoint(new Point(0, 0));
            var rightButtonsRight = origin.X + _titleBarRightButtons.ActualWidth;
            var rightGap = _appTitleBar.ActualWidth - rightButtonsRight;

            _logger.LogDebug(
                "TitleBar metrics Source={Source} LeftInset={LeftInset} RightInset={RightInset} TitleBarHeight={TitleBarHeight} TitleBarWidth={TitleBarWidth} RightButtonsX={RightButtonsX} RightButtonsWidth={RightButtonsWidth} RightButtonsRight={RightButtonsRight} RightGap={RightGap}",
                source,
                _appWindowTitleBar.LeftInset,
                _appWindowTitleBar.RightInset,
                _appWindowTitleBar.Height,
                _appTitleBar.ActualWidth,
                origin.X,
                _titleBarRightButtons.ActualWidth,
                rightButtonsRight,
                rightGap);
            App.BootLog(
                "TitleBarMetrics "
                + "Source=" + source
                + " LeftInset=" + _appWindowTitleBar.LeftInset
                + " RightInset=" + _appWindowTitleBar.RightInset
                + " TitleBarHeight=" + _appWindowTitleBar.Height
                + " TitleBarWidth=" + _appTitleBar.ActualWidth
                + " RightButtonsX=" + origin.X
                + " RightButtonsWidth=" + _titleBarRightButtons.ActualWidth
                + " RightButtonsRight=" + rightButtonsRight
                + " RightGap=" + rightGap);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "TitleBar metrics capture failed Source={Source}", source);
            App.BootLog("TitleBarMetricsCaptureFailed Source=" + source + " Exception=" + ex.GetType().Name);
        }
#endif
    }

    private void RefreshInteractiveRegions()
    {
        if (_winuiTitleBarControl is not null)
        {
            return;
        }

        UpdateInteractiveRegions();
        _ = _dispatcherQueue.TryEnqueue(UpdateInteractiveRegions);
    }

    private void AttachTitleBarXamlRootChanged()
    {
        var xamlRoot = _appTitleBar.XamlRoot;
        if (ReferenceEquals(_observedTitleBarXamlRoot, xamlRoot))
        {
            return;
        }

        if (_observedTitleBarXamlRoot is not null)
        {
            _observedTitleBarXamlRoot.Changed -= OnTitleBarXamlRootChanged;
        }

        _observedTitleBarXamlRoot = xamlRoot;
        if (_observedTitleBarXamlRoot is not null)
        {
            _observedTitleBarXamlRoot.Changed += OnTitleBarXamlRootChanged;
        }
    }

    private void DetachTitleBarXamlRootChanged()
    {
        if (_observedTitleBarXamlRoot is null)
        {
            return;
        }

        _observedTitleBarXamlRoot.Changed -= OnTitleBarXamlRootChanged;
        _observedTitleBarXamlRoot = null;
    }

    private void OnTitleBarXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        AttachTitleBarXamlRootChanged();
        RefreshInteractiveRegions();
    }

    private void UpdateInteractiveRegions()
    {
        if (_titleBarPointerSource is null || _appTitleBar.XamlRoot is null)
        {
            return;
        }

        var regions = new List<RectInt32>();

        TryAddInteractiveRegion(_titleBarLeftButtons, regions);
        TryAddInteractiveRegion(_titleBarSearchBox, regions);
        TryAddInteractiveRegion(_titleBarRightButtons, regions);

        _titleBarPointerSource.SetRegionRects(NonClientRegionKind.Passthrough, regions.ToArray());
    }

    private void TryAddInteractiveRegion(FrameworkElement element, List<RectInt32> regions)
    {
        if (element.Visibility != Visibility.Visible || element.ActualWidth <= 0 || element.ActualHeight <= 0)
        {
            return;
        }

        var transform = element.TransformToVisual(_appTitleBar);
        var origin = transform.TransformPoint(new Point(0, 0));
        var scale = _appTitleBar.XamlRoot?.RasterizationScale ?? 1.0;

        regions.Add(new RectInt32(
            (int)Math.Round(origin.X * scale),
            (int)Math.Round(origin.Y * scale),
            Math.Max(1, (int)Math.Round(element.ActualWidth * scale)),
            Math.Max(1, (int)Math.Round(element.ActualHeight * scale))));
    }

    private static void DebugBootLog(string message)
    {
#if DEBUG
        App.BootLog(message);
#endif
    }
#endif
}
