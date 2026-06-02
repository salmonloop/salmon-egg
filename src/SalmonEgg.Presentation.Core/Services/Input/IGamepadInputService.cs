using System;

namespace SalmonEgg.Presentation.Core.Services.Input;

public interface IGamepadInputService : IDisposable
{
    event EventHandler<GamepadNavigationIntent>? IntentRaised;

    event EventHandler<GamepadShortcutIntent>? ShortcutRaised;

    event EventHandler<GamepadContextIntent>? ContextIntentRaised;

    void Start();

    void Stop();
}
