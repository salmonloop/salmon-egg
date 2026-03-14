using System;

namespace SalmonEgg.Presentation.Services;

public sealed class NoopFloatingChatWindowService : IFloatingChatWindowService
{
    public event EventHandler<bool> OpenStateChanged
    {
        add { }
        remove { }
    }

    public event EventHandler<bool> AlwaysOnTopChanged
    {
        add { }
        remove { }
    }

    public bool IsOpen => false;

    public bool IsAlwaysOnTop
    {
        get => false;
        set { }
    }

    public void Toggle()
    {
    }

    public void Show()
    {
    }

    public void Hide()
    {
    }
}
