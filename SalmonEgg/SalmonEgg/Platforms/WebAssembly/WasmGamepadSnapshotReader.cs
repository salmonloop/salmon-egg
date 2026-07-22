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
            ActiveShortcuts: GamepadShortcutIntentProjector.GetActiveShortcuts(reading),
            StandardGamepads: CreateStandardGamepadDiagnostics(readings),
            RawControllers: []);
    }


    private static IReadOnlyList<StandardGamepadDiagnostics> CreateStandardGamepadDiagnostics(
        IReadOnlyList<GamepadInputReading> readings)
    {
        if (readings.Count == 0)
        {
            return [];
        }

        var diagnostics = new List<StandardGamepadDiagnostics>(readings.Count);
        foreach (var reading in readings)
        {
            diagnostics.Add(new StandardGamepadDiagnostics(
                FaceButtonLayout: RawGameControllerFaceButtonLayout.Standard,
                ButtonLabels: [],
                PressedButtons: [],
                Reading: reading));
        }

        return diagnostics;
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
        var mapping = SafeGetString(gamepad, "mapping");

        return BrowserGamepadInputReadingMapper.GetInputReading(
            mapping,
            ReadButtons(buttons),
            ReadAxes(axes));
    }

    private static IReadOnlyList<BrowserGamepadButtonReading> ReadButtons(JSObject? buttons)
    {
        if (buttons is null)
        {
            return [];
        }

        var length = SafeGetInt32(buttons, "length");
        if (length <= 0)
        {
            return [];
        }

        var readings = new List<BrowserGamepadButtonReading>(length);
        for (var index = 0; index < length; index++)
        {
            using var button = SafeGetObject(buttons, index.ToString(System.Globalization.CultureInfo.InvariantCulture));
            readings.Add(button is null
                ? default
                : new BrowserGamepadButtonReading(
                    Pressed: SafeGetBoolean(button, "pressed"),
                    Value: SafeGetDouble(button, "value")));
        }

        return readings;
    }

    private static IReadOnlyList<double> ReadAxes(JSObject? axes)
    {
        if (axes is null)
        {
            return [];
        }

        var length = SafeGetInt32(axes, "length");
        if (length <= 0)
        {
            return [];
        }

        var readings = new List<double>(length);
        for (var index = 0; index < length; index++)
        {
            readings.Add(SafeGetDouble(axes, index.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        return readings;
    }

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

    private static string SafeGetString(JSObject value, string propertyName)
    {
        try
        {
            return value.GetPropertyAsString(propertyName) ?? string.Empty;
        }
        catch (JSException)
        {
            return string.Empty;
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
}
#endif
