using System.Linq;
using SalmonEgg.Presentation.Core.Services.Input;

namespace SalmonEgg.Presentation.Core.Tests.Input;

public sealed class StandardGamepadInputReadingMapperTests
{
    [Fact]
    public void GetInputReading_ProjectsStandardButtonsToCommonSemanticReading()
    {
        var reading = StandardGamepadInputReadingMapper.GetInputReading(
            moveUp: true,
            moveDown: false,
            moveLeft: true,
            moveRight: false,
            activate: true,
            back: true,
            shortcutVoiceToggle: true,
            leftTrigger: 0.75,
            rightTrigger: 0,
            thumbstickX: 0,
            thumbstickY: 0);

        Assert.Equal(
            [
                GamepadNavigationIntent.MoveUp,
                GamepadNavigationIntent.MoveLeft,
                GamepadNavigationIntent.Activate,
                GamepadNavigationIntent.Back
            ],
            GamepadIntentProcessor.GetActiveIntents(reading).OrderBy(static intent => intent));
        Assert.Equal([GamepadShortcutIntent.ToggleVoiceInput], GamepadShortcutIntentProjector.GetActiveShortcuts(reading));
        Assert.Equal([GamepadContextIntent.PageUp], GamepadContextIntentProjector.GetActiveIntents(reading));
    }

    [Fact]
    public void GetInputReading_ClampsAnalogValuesBeforeProjection()
    {
        var reading = StandardGamepadInputReadingMapper.GetInputReading(
            moveUp: false,
            moveDown: false,
            moveLeft: false,
            moveRight: false,
            activate: false,
            back: false,
            shortcutVoiceToggle: false,
            leftTrigger: 2,
            rightTrigger: double.NaN,
            thumbstickX: -2,
            thumbstickY: double.NaN);

        Assert.Equal(1, reading.LeftTrigger);
        Assert.Equal(0, reading.RightTrigger);
        Assert.Equal(-1, reading.ThumbstickX);
        Assert.Equal(0, reading.ThumbstickY);
    }
}
