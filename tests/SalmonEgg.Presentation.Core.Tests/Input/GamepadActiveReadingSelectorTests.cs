using SalmonEgg.Presentation.Core.Services.Input;

namespace SalmonEgg.Presentation.Core.Tests.Input;

public sealed class GamepadActiveReadingSelectorTests
{
    [Fact]
    public void TrySelectActiveReading_PrefersStandardGamepad_WhenBothPathsAreActive()
    {
        var gamepadReadings = new[]
        {
            new GamepadInputReading(
                MoveUp: false,
                MoveDown: true,
                MoveLeft: false,
                MoveRight: false,
                Activate: false,
                Back: false)
        };
        var rawReadings = new[]
        {
            new GamepadInputReading(
                MoveUp: true,
                MoveDown: false,
                MoveLeft: false,
                MoveRight: false,
                Activate: false,
                Back: false)
        };

        var selected = GamepadActiveReadingSelector.TrySelectActiveReading(gamepadReadings, rawReadings, out var selection);

        Assert.True(selected);
        Assert.Equal(GamepadInputPath.Gamepad, selection.InputPath);
        Assert.Equal(gamepadReadings[0], selection.Reading);
    }

    [Fact]
    public void TrySelectActiveReading_FallsBackToRaw_WhenStandardGamepadIsIdle()
    {
        var gamepadReadings = new[]
        {
            default(GamepadInputReading)
        };
        var rawReadings = new[]
        {
            new GamepadInputReading(
                MoveUp: false,
                MoveDown: true,
                MoveLeft: false,
                MoveRight: false,
                Activate: false,
                Back: false)
        };

        var selected = GamepadActiveReadingSelector.TrySelectActiveReading(gamepadReadings, rawReadings, out var selection);

        Assert.True(selected);
        Assert.Equal(GamepadInputPath.RawGameController, selection.InputPath);
        Assert.Equal(rawReadings[0], selection.Reading);
    }

    [Fact]
    public void TrySelectActiveReading_ReturnsFalse_WhenBothPathsAreIdle()
    {
        var selected = GamepadActiveReadingSelector.TrySelectActiveReading(
            [default],
            [default],
            out var selection);

        Assert.False(selected);
        Assert.Equal(GamepadInputPath.None, selection.InputPath);
        Assert.Equal(default, selection.Reading);
    }

    [Fact]
    public void TrySelectActiveReading_UsesFirstActiveReading_WithinEachPath()
    {
        var gamepadReadings = new[]
        {
            default(GamepadInputReading),
            new GamepadInputReading(
                MoveUp: true,
                MoveDown: false,
                MoveLeft: false,
                MoveRight: false,
                Activate: false,
                Back: false),
            new GamepadInputReading(
                MoveUp: false,
                MoveDown: true,
                MoveLeft: false,
                MoveRight: false,
                Activate: false,
                Back: false)
        };

        var selected = GamepadActiveReadingSelector.TrySelectActiveReading(gamepadReadings, [], out var selection);

        Assert.True(selected);
        Assert.Equal(GamepadInputPath.Gamepad, selection.InputPath);
        Assert.Equal(gamepadReadings[1], selection.Reading);
    }

    [Fact]
    public void TrySelectActiveReading_TreatsShortcutOnlyReadingAsActive()
    {
        var gamepadReadings = new[]
        {
            new GamepadInputReading(
                MoveUp: false,
                MoveDown: false,
                MoveLeft: false,
                MoveRight: false,
                Activate: false,
                Back: false,
                ShortcutVoiceToggle: true)
        };

        var selected = GamepadActiveReadingSelector.TrySelectActiveReading(gamepadReadings, [], out var selection);

        Assert.True(selected);
        Assert.Equal(GamepadInputPath.Gamepad, selection.InputPath);
        Assert.Equal(gamepadReadings[0], selection.Reading);
    }

    [Fact]
    public void TrySelectActiveReading_TreatsTriggerOnlyReadingAsActive()
    {
        var gamepadReadings = new[]
        {
            new GamepadInputReading(
                MoveUp: false,
                MoveDown: false,
                MoveLeft: false,
                MoveRight: false,
                Activate: false,
                Back: false,
                LeftTrigger: 0.75)
        };

        var selected = GamepadActiveReadingSelector.TrySelectActiveReading(gamepadReadings, [], out var selection);

        Assert.True(selected);
        Assert.Equal(GamepadInputPath.Gamepad, selection.InputPath);
        Assert.Equal(gamepadReadings[0], selection.Reading);
    }

    [Fact]
    public void TrySelectActiveReading_TreatsThumbstickOnlyReadingAsActive()
    {
        var gamepadReadings = new[]
        {
            new GamepadInputReading(
                MoveUp: false,
                MoveDown: false,
                MoveLeft: false,
                MoveRight: false,
                Activate: false,
                Back: false,
                ThumbstickX: 0.75,
                ThumbstickY: 0.10)
        };

        var selected = GamepadActiveReadingSelector.TrySelectActiveReading(gamepadReadings, [], out var selection);

        Assert.True(selected);
        Assert.Equal(GamepadInputPath.Gamepad, selection.InputPath);
        Assert.Equal(gamepadReadings[0], selection.Reading);
    }

    [Fact]
    public void TrySelectActiveReading_IdleStandardDoesNotHideActiveRawFaceIntent()
    {
        // Dual-path invariant: an idle standard Gamepad must not suppress an active
        // RawGameController face intent (for example DualSense/Switch raw-only paths).
        var gamepadReadings = new[]
        {
            default(GamepadInputReading),
            default(GamepadInputReading)
        };
        var rawReadings = new[]
        {
            new GamepadInputReading(
                MoveUp: false,
                MoveDown: false,
                MoveLeft: false,
                MoveRight: false,
                Activate: true,
                Back: false)
        };

        var selected = GamepadActiveReadingSelector.TrySelectActiveReading(
            gamepadReadings,
            rawReadings,
            out var selection);

        Assert.True(selected);
        Assert.Equal(GamepadInputPath.RawGameController, selection.InputPath);
        Assert.True(selection.Reading.Activate);
    }

    [Fact]
    public void TrySelectActiveReading_PrefersStandard_WhenStandardTriggerAndRawFaceBothActive()
    {
        // Dual-path: when the standard Gamepad path has any active projection (here LT
        // PageUp), it remains authoritative even if Raw reports a face Activate. This
        // keeps Xbox/DualSense dual-enumeration hosts from double-dispatching.
        var gamepadReadings = new[]
        {
            new GamepadInputReading(
                MoveUp: false,
                MoveDown: false,
                MoveLeft: false,
                MoveRight: false,
                Activate: false,
                Back: false,
                LeftTrigger: 1)
        };
        var rawReadings = new[]
        {
            new GamepadInputReading(
                MoveUp: false,
                MoveDown: false,
                MoveLeft: false,
                MoveRight: false,
                Activate: true,
                Back: false)
        };

        var selected = GamepadActiveReadingSelector.TrySelectActiveReading(
            gamepadReadings,
            rawReadings,
            out var selection);

        Assert.True(selected);
        Assert.Equal(GamepadInputPath.Gamepad, selection.InputPath);
        Assert.Equal(1, selection.Reading.LeftTrigger);
        Assert.False(selection.Reading.Activate);
    }

    [Fact]
    public void TrySelectActiveReading_FallsBackToRaw_WhenStandardIsWestFaceNoOpOnly()
    {
        // A west-face-only standard reading projects no app intents/shortcuts/context.
        // Raw DualSense/Switch face must still be selectable (no silent dual-path hide).
        var gamepadReadings = new[]
        {
            // West no-op is represented as an all-clear reading after Core projection.
            default(GamepadInputReading)
        };
        var rawReadings = new[]
        {
            new GamepadInputReading(
                MoveUp: false,
                MoveDown: false,
                MoveLeft: false,
                MoveRight: false,
                Activate: true,
                Back: false)
        };

        var selected = GamepadActiveReadingSelector.TrySelectActiveReading(
            gamepadReadings,
            rawReadings,
            out var selection);

        Assert.True(selected);
        Assert.Equal(GamepadInputPath.RawGameController, selection.InputPath);
        Assert.True(selection.Reading.Activate);
    }

    [Fact]
    public void TrySelectActiveReading_SelectsFirstActiveRawAmongMultipleControllers()
    {
        // Multi-device raw host: first idle DualSense, second active Switch face.
        // Selection must take the first active raw reading, not invent a brand mix.
        var rawReadings = new[]
        {
            default(GamepadInputReading),
            new GamepadInputReading(
                MoveUp: false,
                MoveDown: false,
                MoveLeft: false,
                MoveRight: false,
                Activate: true,
                Back: false)
        };

        var selected = GamepadActiveReadingSelector.TrySelectActiveReading(
            [],
            rawReadings,
            out var selection);

        Assert.True(selected);
        Assert.Equal(GamepadInputPath.RawGameController, selection.InputPath);
        Assert.True(selection.Reading.Activate);
        Assert.Equal(rawReadings[1], selection.Reading);
    }

    [Fact]
    public void TrySelectActiveReading_PrefersEarlierActiveStandardOverLaterActiveRaw()
    {
        // Dual-enumeration multi-brand: standard Xbox LT active must win over a later
        // raw DualSense Activate even when both paths have active projections.
        var gamepadReadings = new[]
        {
            default(GamepadInputReading),
            new GamepadInputReading(
                MoveUp: false,
                MoveDown: false,
                MoveLeft: false,
                MoveRight: false,
                Activate: false,
                Back: false,
                LeftTrigger: 1)
        };
        var rawReadings = new[]
        {
            new GamepadInputReading(
                MoveUp: false,
                MoveDown: false,
                MoveLeft: false,
                MoveRight: false,
                Activate: true,
                Back: false)
        };

        var selected = GamepadActiveReadingSelector.TrySelectActiveReading(
            gamepadReadings,
            rawReadings,
            out var selection);

        Assert.True(selected);
        Assert.Equal(GamepadInputPath.Gamepad, selection.InputPath);
        Assert.Equal(1, selection.Reading.LeftTrigger);
        Assert.False(selection.Reading.Activate);
    }
}
