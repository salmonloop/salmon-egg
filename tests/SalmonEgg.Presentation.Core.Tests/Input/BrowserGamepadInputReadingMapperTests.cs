using SalmonEgg.Presentation.Core.Services.Input;

namespace SalmonEgg.Presentation.Core.Tests.Input;

public sealed class BrowserGamepadInputReadingMapperTests
{
    [Fact]
    public void GetInputReading_MapsStandardGamepadButtonsAxesAndTriggersToCommonReading()
    {
        var buttons = CreateStandardButtons(
            pressed: [0, 3, 13],
            values: new Dictionary<int, double>
            {
                [6] = 0.75,
                [7] = 0.25
            });

        var reading = BrowserGamepadInputReadingMapper.GetInputReading(
            BrowserGamepadInputReadingMapper.StandardMapping,
            buttons,
            [0.25, 0.50]);

        Assert.True(reading.Activate);
        Assert.True(reading.MoveDown);
        Assert.True(reading.ShortcutVoiceToggle);
        Assert.False(reading.Back);
        Assert.Equal(0.75, reading.LeftTrigger);
        Assert.Equal(0.25, reading.RightTrigger);
        Assert.Equal(0.25, reading.ThumbstickX);
        Assert.Equal(-0.50, reading.ThumbstickY);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("STANDARD")]
    [InlineData("xinput")]
    public void GetInputReading_IgnoresNonStandardMappings(string? mapping)
    {
        var buttons = CreateStandardButtons(pressed: [0, 13], values: new Dictionary<int, double>());

        var reading = BrowserGamepadInputReadingMapper.GetInputReading(mapping, buttons, [1, -1]);

        Assert.Equal(default, reading);
        Assert.Empty(GamepadIntentProcessor.GetActiveIntents(reading));
    }

    [Fact]
    public void GetInputReading_ClampsButtonAndAxisValues()
    {
        var buttons = CreateStandardButtons(
            pressed: [],
            values: new Dictionary<int, double>
            {
                [0] = 0.75,
                [6] = 1.50,
                [7] = double.NaN
            });

        var reading = BrowserGamepadInputReadingMapper.GetInputReading(
            BrowserGamepadInputReadingMapper.StandardMapping,
            buttons,
            [2.0, double.NaN]);

        Assert.True(reading.Activate);
        Assert.Equal(1, reading.LeftTrigger);
        Assert.Equal(0, reading.RightTrigger);
        Assert.Equal(1, reading.ThumbstickX);
        Assert.Equal(0, reading.ThumbstickY);
    }

    [Fact]
    public void GetInputReading_ToleratesShortButtonAndAxisArrays()
    {
        var reading = BrowserGamepadInputReadingMapper.GetInputReading(
            BrowserGamepadInputReadingMapper.StandardMapping,
            [new BrowserGamepadButtonReading(Pressed: true, Value: 1)],
            [0.75]);

        Assert.True(reading.Activate);
        Assert.False(reading.Back);
        Assert.Equal(0.75, reading.ThumbstickX);
        Assert.Equal(0, reading.ThumbstickY);
    }

    [Fact]
    public void GetInputReading_RequiresButtonAndAxisCollections()
    {
        Assert.Throws<ArgumentNullException>(() => BrowserGamepadInputReadingMapper.GetInputReading("standard", null!, []));
        Assert.Throws<ArgumentNullException>(() => BrowserGamepadInputReadingMapper.GetInputReading("standard", [], null!));
    }

    private static BrowserGamepadButtonReading[] CreateStandardButtons(
        IReadOnlyCollection<int> pressed,
        IReadOnlyDictionary<int, double> values)
    {
        var buttons = new BrowserGamepadButtonReading[16];
        for (var index = 0; index < buttons.Length; index++)
        {
            var isPressed = pressed.Contains(index);
            buttons[index] = new BrowserGamepadButtonReading(
                Pressed: isPressed,
                Value: values.TryGetValue(index, out var value) ? value : isPressed ? 1 : 0);
        }

        return buttons;
    }
}
