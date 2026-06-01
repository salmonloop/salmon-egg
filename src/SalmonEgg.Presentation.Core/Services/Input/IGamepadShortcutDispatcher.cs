namespace SalmonEgg.Presentation.Core.Services.Input;

public interface IGamepadShortcutDispatcher
{
    bool TryDispatch(GamepadShortcutIntent intent);
}
