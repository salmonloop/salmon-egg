using System;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using SalmonEgg.Presentation.Core.Services;

namespace SalmonEgg.Presentation.Services;

public sealed partial class AppActivationSignalSource : IApplicationActivationSignalSource, IApplicationVisibilityState
{
    private readonly ApplicationWindowActivityTracker<Window> _activityTracker = new();
    private readonly ILogger<AppActivationSignalSource> _logger;
    private bool _isActive;

    public AppActivationSignalSource(ILogger<AppActivationSignalSource> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public event EventHandler? Activated;
    public event EventHandler? ActivityChanged;

    public Window? ActiveWindow => _activityTracker.ActiveWindow;

    // Notification consumers run outside the UI thread; native Window properties do not.
    public bool IsActive => Volatile.Read(ref _isActive);

    public void Attach(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (!_activityTracker.Attach(window))
        {
            return;
        }

        window.Activated += OnWindowActivated;
        window.Closed += OnWindowClosed;
        window.VisibilityChanged += OnWindowVisibilityChanged;
        window.AppWindow.Changed += OnAppWindowChanged;
        AttachPlatformActivity(window);
        PublishActivity();
    }

    public void Detach(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (!_activityTracker.Detach(window))
        {
            return;
        }

        window.Activated -= OnWindowActivated;
        window.Closed -= OnWindowClosed;
        window.VisibilityChanged -= OnWindowVisibilityChanged;
        window.AppWindow.Changed -= OnAppWindowChanged;
        DetachPlatformActivity(window);
        PublishActivity();
    }

    private void OnWindowActivated(object sender, WindowActivatedEventArgs e)
    {
        if (string.Equals(e.WindowActivationState.ToString(), "Deactivated", StringComparison.Ordinal))
        {
            if (sender is Window deactivatedWindow)
            {
                _activityTracker.Deactivate(deactivatedWindow);
                PublishActivity();
            }

            return;
        }

        if (sender is Window window)
        {
            if (!_activityTracker.Activate(window))
            {
                return;
            }
        }

        PublishActivity();
        Activated?.Invoke(this, EventArgs.Empty);
    }

    private void OnWindowClosed(object sender, WindowEventArgs e)
    {
        if (sender is Window window)
        {
            Detach(window);
        }
    }

    private void OnWindowVisibilityChanged(object sender, object args)
        => PublishActivity();

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
        => PublishActivity();

    private void PublishActivity()
    {
        var window = ActiveWindow;
        var isActive = _activityTracker.IsActive && window is { Visible: true }
            && window.AppWindow.Presenter is not OverlappedPresenter { State: OverlappedPresenterState.Minimized };
        ConstrainPlatformActivity(ref isActive);
        Volatile.Write(ref _isActive, isActive);
        ActivityChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void AttachPlatformActivity(Window window);

    partial void DetachPlatformActivity(Window window);

    partial void ConstrainPlatformActivity(ref bool isActive);
}
