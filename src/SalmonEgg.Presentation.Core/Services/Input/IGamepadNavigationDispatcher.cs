namespace SalmonEgg.Presentation.Core.Services.Input;

public interface IGamepadNavigationDispatcher
{
    bool TryDispatch(GamepadNavigationIntent intent);
}
