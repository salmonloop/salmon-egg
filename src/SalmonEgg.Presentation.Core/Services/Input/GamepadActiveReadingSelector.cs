namespace SalmonEgg.Presentation.Core.Services.Input;

public enum GamepadInputPath
{
    None,
    Gamepad,
    RawGameController
}

public readonly record struct GamepadActiveReadingSelection(
    GamepadInputPath InputPath,
    GamepadInputReading Reading);

public static class GamepadActiveReadingSelector
{
    public static bool TrySelectActiveReading(
        IReadOnlyList<GamepadInputReading> gamepadReadings,
        IReadOnlyList<GamepadInputReading> rawReadings,
        out GamepadActiveReadingSelection selection)
    {
        ArgumentNullException.ThrowIfNull(gamepadReadings);
        ArgumentNullException.ThrowIfNull(rawReadings);

        if (TrySelectFirstActive(gamepadReadings, GamepadInputPath.Gamepad, out selection))
        {
            return true;
        }

        if (TrySelectFirstActive(rawReadings, GamepadInputPath.RawGameController, out selection))
        {
            return true;
        }

        selection = default;
        return false;
    }

    private static bool TrySelectFirstActive(
        IReadOnlyList<GamepadInputReading> readings,
        GamepadInputPath path,
        out GamepadActiveReadingSelection selection)
    {
        foreach (var reading in readings)
        {
            if (GamepadIntentProcessor.GetActiveIntents(reading).Count == 0)
            {
                continue;
            }

            selection = new GamepadActiveReadingSelection(path, reading);
            return true;
        }

        selection = default;
        return false;
    }
}
