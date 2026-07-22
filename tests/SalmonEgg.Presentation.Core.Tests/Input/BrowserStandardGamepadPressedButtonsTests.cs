using SalmonEgg.Presentation.Core.Services.Input;

namespace SalmonEgg.Presentation.Core.Tests.Input;

public sealed class BrowserStandardGamepadPressedButtonsTests
{
    [Fact]
    public void GetPressedNames_MapsStandardFaceAndDpadIndexes()
    {
        var buttons = CreateButtons(pressed: [0, 2, 13]);

        var names = BrowserStandardGamepadPressedButtons.GetPressedNames(
            BrowserGamepadInputReadingMapper.StandardMapping,
            buttons);

        Assert.Equal(["A", "X", "DPadDown"], names);
    }

    [Fact]
    public void GetPressedNames_UsesValueThresholdWhenPressedFlagFalse()
    {
        var buttons = CreateButtons(pressed: [], values: new Dictionary<int, double> { [6] = 0.75 });

        var names = BrowserStandardGamepadPressedButtons.GetPressedNames(
            BrowserGamepadInputReadingMapper.StandardMapping,
            buttons);

        Assert.Equal(["LeftTrigger"], names);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("xinput")]
    public void GetPressedNames_IgnoresNonStandardMapping(string? mapping)
    {
        var buttons = CreateButtons(pressed: [0, 1, 3]);

        Assert.Empty(BrowserStandardGamepadPressedButtons.GetPressedNames(mapping, buttons));
    }

    [Fact]
    public void GetPressedNames_RequiresButtonsCollection()
    {
        Assert.Throws<ArgumentNullException>(() =>
            BrowserStandardGamepadPressedButtons.GetPressedNames(
                BrowserGamepadInputReadingMapper.StandardMapping,
                null!));
    }

    private static BrowserGamepadButtonReading[] CreateButtons(
        IReadOnlyCollection<int> pressed,
        IReadOnlyDictionary<int, double>? values = null)
    {
        values ??= new Dictionary<int, double>();
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
