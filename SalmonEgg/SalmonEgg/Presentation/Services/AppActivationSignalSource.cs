using System;
using Microsoft.UI.Xaml;
using SalmonEgg.Presentation.Core.Services;

namespace SalmonEgg.Presentation.Services;

public sealed class AppActivationSignalSource : IApplicationActivationSignalSource, IApplicationVisibilityState
{
    private readonly ApplicationWindowActivityTracker<Window> _activityTracker = new();

    public event EventHandler? Activated;

    public Window? ActiveWindow => _activityTracker.ActiveWindow;

    public bool IsActive => _activityTracker.IsActive;

    public void Attach(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (!_activityTracker.Attach(window))
        {
            return;
        }

        window.Activated += OnWindowActivated;
        window.Closed += OnWindowClosed;
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
    }

    private void OnWindowActivated(object sender, WindowActivatedEventArgs e)
    {
        if (string.Equals(e.WindowActivationState.ToString(), "Deactivated", StringComparison.Ordinal))
        {
            if (sender is Window deactivatedWindow)
            {
                _activityTracker.Deactivate(deactivatedWindow);
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

        Activated?.Invoke(this, EventArgs.Empty);
    }

    private void OnWindowClosed(object sender, WindowEventArgs e)
    {
        if (sender is Window window)
        {
            Detach(window);
        }
    }
}
