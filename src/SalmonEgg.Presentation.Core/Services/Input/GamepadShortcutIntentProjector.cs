namespace SalmonEgg.Presentation.Core.Services.Input;

public static class GamepadShortcutIntentProjector
{
    private static readonly GamepadShortcutIntent[] VoiceToggleShortcut = [GamepadShortcutIntent.ToggleVoiceInput];

    public static bool HasActiveShortcuts(GamepadInputReading reading)
        => reading.ShortcutVoiceToggle;

    public static IReadOnlyCollection<GamepadShortcutIntent> GetActiveShortcuts(GamepadInputReading reading)
        => reading.ShortcutVoiceToggle
            ? VoiceToggleShortcut
            : [];
}
