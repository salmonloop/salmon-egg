using System;
using System.Collections.Generic;

namespace SalmonEgg.Presentation.Core.Services.Input;

/// <summary>
/// Projects dual-path active reading into Diagnostics snapshot fields.
/// Platform diagnostics services collect device rows; Core owns path selection and active intents.
/// </summary>
public static class GamepadDiagnosticsActiveReadingProjector
{
    public static GamepadDiagnosticsActiveProjection Project(
        IReadOnlyList<GamepadInputReading> gamepadReadings,
        IReadOnlyList<GamepadInputReading> rawReadings)
    {
        ArgumentNullException.ThrowIfNull(gamepadReadings);
        ArgumentNullException.ThrowIfNull(rawReadings);

        if (!GamepadActiveReadingSelector.TrySelectActiveReading(gamepadReadings, rawReadings, out var selection))
        {
            return GamepadDiagnosticsActiveProjection.None;
        }

        return new GamepadDiagnosticsActiveProjection(
            InputSource: ToInputSource(selection.InputPath),
            Reading: selection.Reading,
            ActiveIntents: GamepadIntentProcessor.GetActiveIntents(selection.Reading),
            ActiveContextIntents: GamepadContextIntentProjector.GetActiveIntents(selection.Reading),
            ActiveShortcuts: GamepadShortcutIntentProjector.GetActiveShortcuts(selection.Reading));
    }

    public static GamepadDiagnosticsInputSource ToInputSource(GamepadInputPath path)
        => path switch
        {
            GamepadInputPath.Gamepad => GamepadDiagnosticsInputSource.Gamepad,
            GamepadInputPath.RawGameController => GamepadDiagnosticsInputSource.RawGameController,
            _ => GamepadDiagnosticsInputSource.None
        };
}

public readonly record struct GamepadDiagnosticsActiveProjection(
    GamepadDiagnosticsInputSource InputSource,
    GamepadInputReading Reading,
    IReadOnlyCollection<GamepadNavigationIntent> ActiveIntents,
    IReadOnlyCollection<GamepadContextIntent> ActiveContextIntents,
    IReadOnlyCollection<GamepadShortcutIntent> ActiveShortcuts)
{
    public static GamepadDiagnosticsActiveProjection None { get; } = new(
        InputSource: GamepadDiagnosticsInputSource.None,
        Reading: default,
        ActiveIntents: Array.Empty<GamepadNavigationIntent>(),
        ActiveContextIntents: Array.Empty<GamepadContextIntent>(),
        ActiveShortcuts: Array.Empty<GamepadShortcutIntent>());
}
