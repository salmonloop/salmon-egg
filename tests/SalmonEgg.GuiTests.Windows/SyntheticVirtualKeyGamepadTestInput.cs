namespace SalmonEgg.GuiTests.Windows;

internal sealed class SyntheticVirtualKeyGamepadTestInput : IGamepadTestInput
{
    private readonly WindowsGuiAppSession _session;

    public SyntheticVirtualKeyGamepadTestInput(WindowsGuiAppSession session)
    {
        _session = session;
    }

    public void PressUp()
    {
        _session.PressSyntheticGamepadUp();
    }

    public void PressDown()
    {
        _session.PressSyntheticGamepadDown();
    }

    public void PressLeft()
    {
        _session.PressSyntheticGamepadLeft();
    }

    public void PressRight()
    {
        _session.PressSyntheticGamepadRight();
    }

    public void PressActivate()
    {
        _session.PressSyntheticGamepadActivate();
    }

    public void PressBack()
    {
        _session.PressSyntheticGamepadBack();
    }

    public void Dispose()
    {
    }
}
