using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using SalmonEgg.Presentation.Core.Services;

namespace SalmonEgg.Presentation.Services;

public sealed class AppActivationSignalSource : IApplicationActivationSignalSource
{
    private readonly object _sync = new();
    private readonly HashSet<Window> _attachedWindows = new();

    public event EventHandler? Activated;

    public void Attach(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        lock (_sync)
        {
            if (!_attachedWindows.Add(window))
            {
                return;
            }
        }

        window.Activated += OnWindowActivated;
        window.Closed += OnWindowClosed;
    }

    public void Detach(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        lock (_sync)
        {
            if (!_attachedWindows.Remove(window))
            {
                return;
            }
        }

        window.Activated -= OnWindowActivated;
        window.Closed -= OnWindowClosed;
    }

    private void OnWindowActivated(object sender, WindowActivatedEventArgs e)
    {
        if (string.Equals(e.WindowActivationState.ToString(), "Deactivated", StringComparison.Ordinal))
        {
            return;
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
