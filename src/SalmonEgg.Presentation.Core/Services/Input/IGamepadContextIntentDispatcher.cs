namespace SalmonEgg.Presentation.Core.Services.Input;

public interface IGamepadContextIntentDispatcher
{
    bool TryDispatch(GamepadContextIntent intent);
}
