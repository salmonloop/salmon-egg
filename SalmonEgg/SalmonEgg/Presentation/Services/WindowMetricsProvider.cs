using Microsoft.UI.Xaml;
using SalmonEgg.Presentation.Core.Services;

namespace SalmonEgg.Presentation.Services;

public sealed class WindowMetricsProvider
{
    private readonly IShellLayoutMetricsSink _sink;

    public WindowMetricsProvider(IShellLayoutMetricsSink sink)
    {
        _sink = sink;
    }

    private Window? _window;
    private ITitleBarInsetProvider? _titleBarInsetProvider;
    private FrameworkElement? _contentRoot;

    public void Attach(Window window, ITitleBarInsetProvider? titleBarInsetProvider)
    {
        Detach();

        _window = window;
        _titleBarInsetProvider = titleBarInsetProvider;
        _contentRoot = _window.Content as FrameworkElement;

        _window.SizeChanged += OnSizeChanged;
        _window.Activated += OnActivated;
        if (_contentRoot != null)
        {
            _contentRoot.SizeChanged += OnContentRootSizeChanged;
        }

        // Initial report
        ReportWindowMetrics(_window.Bounds.Width, _window.Bounds.Height);

        if (_titleBarInsetProvider != null)
        {
            ReportTitleBarInsets();
        }
    }

    public void Detach()
    {
        if (_window != null)
        {
            _window.SizeChanged -= OnSizeChanged;
            _window.Activated -= OnActivated;
        }
        if (_contentRoot != null)
        {
            _contentRoot.SizeChanged -= OnContentRootSizeChanged;
        }

        _window = null;
        _titleBarInsetProvider = null;
        _contentRoot = null;
    }

    private void OnSizeChanged(object sender, WindowSizeChangedEventArgs e)
    {
        ReportWindowMetrics(e.Size.Width, e.Size.Height);
    }

    private void OnActivated(object sender, WindowActivatedEventArgs e)
    {
        if (_titleBarInsetProvider != null)
        {
            ReportTitleBarInsets();
        }
    }

    private void OnContentRootSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_window is null)
        {
            return;
        }

        ReportWindowMetrics(_window.Bounds.Width, _window.Bounds.Height);
    }

    private void ReportTitleBarInsets()
    {
        if (_titleBarInsetProvider is null)
        {
            return;
        }

        var (left, right, height) = _titleBarInsetProvider.GetInsets();
        _ = _sink.ReportTitleBarInsets(left, right, height);
    }

    private void ReportWindowMetrics(double width, double height)
    {
        var content = _window?.Content as FrameworkElement;
        var contentActualWidth = content?.ActualWidth ?? 0;
        var contentActualHeight = content?.ActualHeight ?? 0;
        var (effectiveWidth, effectiveHeight) = ShellLayoutMetricsNormalizer.ResolveEffectiveSize(
            width,
            height,
            contentActualWidth,
            contentActualHeight);

        _ = _sink.ReportWindowMetrics(width, height, effectiveWidth, effectiveHeight);
    }
}
