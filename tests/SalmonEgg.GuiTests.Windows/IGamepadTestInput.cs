namespace SalmonEgg.GuiTests.Windows;

internal interface IGamepadTestInput : IDisposable
{
    void PressUp();

    void PressDown();

    void PressLeft();

    void PressRight();

    void PressActivate();

    void PressBack();

    void PressShortcutVoiceToggle();
}
