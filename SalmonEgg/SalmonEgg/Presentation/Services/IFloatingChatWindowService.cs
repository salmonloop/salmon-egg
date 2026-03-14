using System;

namespace SalmonEgg.Presentation.Services;

public interface IFloatingChatWindowService
{
    event EventHandler<bool> OpenStateChanged;
    event EventHandler<bool> AlwaysOnTopChanged;
    bool IsOpen { get; }
    bool IsAlwaysOnTop { get; set; }
    void Toggle();
    void Show();
    void Hide();
}
