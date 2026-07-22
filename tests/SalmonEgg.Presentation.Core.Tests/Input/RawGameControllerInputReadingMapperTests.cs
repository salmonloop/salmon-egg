using System;
using System.Linq;
using SalmonEgg.Presentation.Core.Services.Input;

namespace SalmonEgg.Presentation.Core.Tests.Input;

public sealed class RawGameControllerInputReadingMapperTests
{
    [Fact]
    public void GetInputReading_ProjectsButtonsSwitchesAndAxesThroughCommonSemanticReading()
    {
        var reading = RawGameControllerInputReadingMapper.GetInputReading(
            [RawGameControllerButtonLabel.LetterB, RawGameControllerButtonLabel.LetterX, RawGameControllerButtonLabel.LeftTrigger],
            [GamepadDirectionalSwitchPosition.DownRight],
            [0.875, 0.45],
            RawGameControllerFaceButtonLayout.Nintendo);

        Assert.Equal(
            [
                GamepadNavigationIntent.MoveDown,
                GamepadNavigationIntent.MoveRight,
                GamepadNavigationIntent.Activate
            ],
            GamepadIntentProcessor.GetActiveIntents(reading).OrderBy(static intent => intent));
        Assert.Equal([GamepadShortcutIntent.ToggleVoiceInput], GamepadShortcutIntentProjector.GetActiveShortcuts(reading));
        Assert.Equal([GamepadContextIntent.PageUp], GamepadContextIntentProjector.GetActiveIntents(reading));
        Assert.Equal(0.75, reading.ThumbstickX, precision: 10);
        Assert.Equal(0.10, reading.ThumbstickY, precision: 10);
    }

    [Fact]
    public void GetInputReading_IgnoresIdleAxesWithoutCreatingThumbstickIntent()
    {
        var reading = RawGameControllerInputReadingMapper.GetInputReading(
            [],
            [],
            [0.0, 0.0]);

        Assert.Equal(default, reading);
        Assert.Empty(GamepadIntentProcessor.GetActiveIntents(reading));
    }

    [Fact]
    public void GetInputReading_TreatsNonFiniteAxesAsIdleWithoutCreatingThumbstickIntent()
    {
        var reading = RawGameControllerInputReadingMapper.GetInputReading(
            [],
            [],
            [double.NaN, double.PositiveInfinity, double.NegativeInfinity]);

        Assert.Equal(default, reading);
        Assert.Empty(GamepadIntentProcessor.GetActiveIntents(reading));
    }

    [Fact]
    public void GetInputReading_RequiresInputs()
    {
        Assert.Throws<ArgumentNullException>(() => RawGameControllerInputReadingMapper.GetInputReading(null!, [], []));
        Assert.Throws<ArgumentNullException>(() => RawGameControllerInputReadingMapper.GetInputReading([], null!, []));
        Assert.Throws<ArgumentNullException>(() => RawGameControllerInputReadingMapper.GetInputReading([], [], null!));
    }

    [Fact]
    public void GetInputReading_WithLetterLabelsAndStandardIdentity_UsesNintendoFaceSemantics()
    {
        var reading = RawGameControllerInputReadingMapper.GetInputReading(
            [RawGameControllerButtonLabel.LetterA, RawGameControllerButtonLabel.LetterX],
            [],
            [],
            RawGameControllerFaceButtonLayout.Standard);

        Assert.Equal([GamepadNavigationIntent.Back], GamepadIntentProcessor.GetActiveIntents(reading));
        Assert.Equal([GamepadShortcutIntent.ToggleVoiceInput], GamepadShortcutIntentProjector.GetActiveShortcuts(reading));
    }
}
