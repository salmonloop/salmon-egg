namespace SalmonEgg.Presentation.Core.Services.Input;

public interface IGamepadShortcutConsumer
{
    bool TryConsumeShortcutIntent(GamepadShortcutIntent intent);
}
