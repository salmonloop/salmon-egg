namespace SalmonEgg.Presentation.Core.Services.Input;

public interface IGamepadContextIntentConsumer
{
    bool TryConsumeContextIntent(GamepadContextIntent intent);
}
