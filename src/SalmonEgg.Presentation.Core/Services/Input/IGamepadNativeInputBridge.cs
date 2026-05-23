namespace SalmonEgg.Presentation.Core.Services.Input;

public interface IGamepadNativeInputBridge
{
    bool TryDispatch(GamepadNavigationIntent intent);
}
