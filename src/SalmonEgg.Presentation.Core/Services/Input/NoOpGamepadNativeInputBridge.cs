namespace SalmonEgg.Presentation.Core.Services.Input;

public sealed class NoOpGamepadNativeInputBridge : IGamepadNativeInputBridge
{
    public bool TryDispatch(GamepadNavigationIntent intent)
    {
        return false;
    }
}
