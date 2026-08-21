using System.Collections.Generic;

namespace SalmonEgg.Presentation.Core.Services;

/// <summary>
/// Tracks whether any attached native window is active without depending on UI types.
/// </summary>
public sealed class ApplicationWindowActivityTracker<TWindow>
    where TWindow : notnull
{
    private readonly object _sync = new();
    private readonly HashSet<TWindow> _attachedWindows = new();
    private readonly HashSet<TWindow> _activeWindows = new();
    private TWindow? _activeWindow;

    public TWindow? ActiveWindow
    {
        get
        {
            lock (_sync)
            {
                return _activeWindow;
            }
        }
    }

    public bool IsActive
    {
        get
        {
            lock (_sync)
            {
                return _activeWindows.Count > 0;
            }
        }
    }

    public bool Attach(TWindow window)
    {
        lock (_sync)
        {
            if (!_attachedWindows.Add(window))
            {
                return false;
            }

            // The main window is attached after its first native activation event.
            // Later windows must wait for their own activation event.
            if (_attachedWindows.Count == 1)
            {
                _activeWindow = window;
                _activeWindows.Add(window);
            }

            return true;
        }
    }

    public bool Activate(TWindow window)
    {
        lock (_sync)
        {
            if (!_attachedWindows.Contains(window))
            {
                return false;
            }

            _activeWindow = window;
            _activeWindows.Add(window);
            return true;
        }
    }

    public bool Deactivate(TWindow window)
    {
        lock (_sync)
        {
            if (!_attachedWindows.Contains(window))
            {
                return false;
            }

            _activeWindows.Remove(window);
            if (EqualityComparer<TWindow>.Default.Equals(_activeWindow, window))
            {
                _activeWindow = FindActiveWindowLocked();
            }

            return true;
        }
    }

    public bool Detach(TWindow window)
    {
        lock (_sync)
        {
            if (!_attachedWindows.Remove(window))
            {
                return false;
            }

            _activeWindows.Remove(window);
            if (EqualityComparer<TWindow>.Default.Equals(_activeWindow, window))
            {
                _activeWindow = FindActiveWindowLocked();
            }

            return true;
        }
    }

    private TWindow? FindActiveWindowLocked()
    {
        foreach (var window in _activeWindows)
        {
            return window;
        }

        return default;
    }
}
