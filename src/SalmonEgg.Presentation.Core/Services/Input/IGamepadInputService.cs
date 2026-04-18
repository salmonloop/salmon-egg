using System;

namespace SalmonEgg.Presentation.Core.Services.Input;

public interface IGamepadInputService : IDisposable
{
    event EventHandler<GamepadNavigationIntent>? IntentRaised;

    void Start();

    void Stop();
}
