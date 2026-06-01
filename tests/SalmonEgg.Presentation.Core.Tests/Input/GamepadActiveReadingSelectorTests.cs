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
}
