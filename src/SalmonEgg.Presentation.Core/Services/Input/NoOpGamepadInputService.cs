using System;

namespace SalmonEgg.Presentation.Core.Services.Input;

public sealed class NoOpGamepadInputService : IGamepadInputService
{
    public event EventHandler<GamepadNavigationIntent>? IntentRaised
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
