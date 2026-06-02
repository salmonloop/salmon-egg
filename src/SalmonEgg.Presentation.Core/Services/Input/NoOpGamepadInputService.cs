using System;

namespace SalmonEgg.Presentation.Core.Services.Input;

public sealed class NoOpGamepadInputService : IGamepadInputService
{
    public event EventHandler<GamepadNavigationIntent>? IntentRaised
    {
        add { }
        remove { }
    }

    public event EventHandler<GamepadShortcutIntent>? ShortcutRaised
    {
        add { }
        remove { }
    }

    public event EventHandler<GamepadContextIntent>? ContextIntentRaised
    {
        add { }
        remove { }
    }

    public void Start()
    {
    }

    public void Stop()
    {
    }

    public void Dispose()
    {
    }
}
