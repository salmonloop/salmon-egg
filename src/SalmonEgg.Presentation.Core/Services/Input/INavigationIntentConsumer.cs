namespace SalmonEgg.Presentation.Core.Services.Input;

public interface INavigationIntentConsumer
{
    bool TryConsumeNavigationIntent(GamepadNavigationIntent intent);
}
