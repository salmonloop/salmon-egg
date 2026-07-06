#if __WASM__
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using SalmonEgg.Presentation.Core.Services.Input;

namespace SalmonEgg.Platforms.WebAssembly;

[SupportedOSPlatform("browser")]
internal static partial class WasmGamepadSnapshotReader
{
    private const double ButtonPressedThreshold = 0.5;

    public static GamepadDiagnosticsSnapshot ReadSnapshot()
    {
        var readings = ReadInputReadings();
        var source = GamepadDiagnosticsInputSource.None;
        var reading = default(GamepadInputReading);

        foreach (var candidate in readings)
        {
            if (GamepadIntentProcessor.GetActiveIntents(candidate).Count == 0
                && !GamepadContextIntentProjector.HasActiveIntents(candidate)
                && !GamepadShortcutIntentProjector.HasActiveShortcuts(candidate))
            {
                continue;
            }

            source = GamepadDiagnosticsInputSource.Gamepad;
            reading = candidate;
            break;
        }

        return new GamepadDiagnosticsSnapshot(
            IsSupported: true,
            ConnectedGamepadCount: readings.Count,
            ConnectedRawControllerCount: 0,
            InputSource: source,
            Reading: reading,
            ActiveIntents: GamepadIntentProcessor.GetActiveIntents(reading),
            ActiveContextIntents: GamepadContextIntentProjector.GetActiveIntents(reading),
            RawControllers: []);
    }

    public static IReadOnlyList<GamepadInputReading> ReadInputReadings()
    {
        using var gamepads = GetGamepads();
        if (gamepads is null)
        {
            return [];
        }

        var length = SafeGetInt32(gamepads, "length");
        if (length <= 0)
        {
            return [];
        }

        var readings = new List<GamepadInputReading>(length);
        for (var i = 0; i < length; i++)
        {
            using var gamepad = SafeGetObject(gamepads, i.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (gamepad is null)
            {
                continue;
            }

            readings.Add(ReadInputReading(gamepad));
        }

        return readings;
    }

    [JSImport("globalThis.navigator.getGamepads")]
    private static partial JSObject? GetGamepads();

    private static GamepadInputReading ReadInputReading(JSObject gamepad)
    {
        using var buttons = SafeGetObject(gamepad, "buttons");
        using var axes = SafeGetObject(gamepad, "axes");

        return new GamepadInputReading(
            MoveUp: IsButtonPressed(buttons, 12),
            MoveDown: IsButtonPressed(buttons, 13),
            MoveLeft: IsButtonPressed(buttons, 14),
            MoveRight: IsButtonPressed(buttons, 15),
            Activate: IsButtonPressed(buttons, 0),
            Back: IsButtonPressed(buttons, 1),
            ShortcutVoiceToggle: IsButtonPressed(buttons, 3),
            LeftTrigger: GetButtonValue(buttons, 6),
            RightTrigger: GetButtonValue(buttons, 7),
            ThumbstickX: GetAxisValue(axes, 0),
            ThumbstickY: -GetAxisValue(axes, 1));
    }

    private static bool IsButtonPressed(JSObject? buttons, int index)
    {
        using var button = SafeGetObject(buttons, index.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (button is null)
        {
            return false;
        }

        return SafeGetBoolean(button, "pressed") || GetButtonValue(button) >= ButtonPressedThreshold;
    }

    private static double GetButtonValue(JSObject? buttons, int index)
    {
        using var button = SafeGetObject(buttons, index.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return button is null ? 0 : GetButtonValue(button);
    }

    private static double GetButtonValue(JSObject button)
        => ClampUnit(SafeGetDouble(button, "value"));

    private static double GetAxisValue(JSObject? axes, int index)
        => ClampSigned(SafeGetDouble(axes, index.ToString(System.Globalization.CultureInfo.InvariantCulture)));

    private static JSObject? SafeGetObject(JSObject? value, string propertyName)
    {
        if (value is null)
        {
            return null;
        }

        try
        {
            return value.GetPropertyAsJSObject(propertyName);
        }
        catch (JSException)
        {
            return null;
        }
    }

    private static bool SafeGetBoolean(JSObject value, string propertyName)
    {
        try
        {
            return value.GetPropertyAsBoolean(propertyName);
        }
        catch (JSException)
        {
            return false;
        }
    }

    private static int SafeGetInt32(JSObject value, string propertyName)
    {
        try
        {
            return value.GetPropertyAsInt32(propertyName);
        }
        catch (JSException)
        {
            return 0;
        }
    }

    private static double SafeGetDouble(JSObject? value, string propertyName)
    {
        if (value is null)
        {
            return 0;
        }

        try
        {
            return value.GetPropertyAsDouble(propertyName);
        }
        catch (JSException)
        {
            return 0;
        }
    }

    private static double ClampUnit(double value)
    {
        if (double.IsNaN(value))
        {
            return 0;
        }

        return Math.Clamp(value, 0, 1);
    }

    private static double ClampSigned(double value)
    {
        if (double.IsNaN(value))
        {
            return 0;
        }

        return Math.Clamp(value, -1, 1);
    }
}
#endif
