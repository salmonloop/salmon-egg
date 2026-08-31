using SalmonEgg.Presentation.Core.Services.Input;

namespace SalmonEgg.Presentation.Core.Tests.Input;

/// <summary>
/// Browser Gamepad API with mapping "standard" is position-based for every controller
/// family. Identity (VID/name) must not invent a second brand semantic path.
/// </summary>
public sealed class BrowserStandardGamepadBrandSemanticsTests
{
    public static IEnumerable<object[]> BrandIds()
    {
        yield return
        [
            "Xbox Wireless Controller (STANDARD GAMEPAD Vendor: 045e Product: 0b13)"
        ];
        yield return
        [
            "DualSense Wireless Controller (STANDARD GAMEPAD Vendor: 054c Product: 0ce6)"
        ];
        yield return
        [
            "Pro Controller (STANDARD GAMEPAD Vendor: 057e Product: 2009)"
        ];
        yield return
        [
            "054c-0ce6-DualSense Wireless Controller"
        ];
    }

    [Theory]
    [MemberData(nameof(BrandIds))]
    public void StandardMapping_ProjectsSameFaceAndTriggerSemanticsForEveryBrandId(string gamepadId)
    {
        var identity = BrowserGamepadIdentityParser.Parse(gamepadId);
        Assert.False(string.IsNullOrWhiteSpace(identity.DisplayName));

        // Identity may mark Nintendo layout for Switch Pro diagnostics labeling only.
        _ = RawGameControllerFaceButtonLayoutResolver.Resolve(
            identity.DisplayName,
            identity.HardwareVendorId,
            labels: default);

        AssertFace(0, activate: true, back: false, voice: false);
        AssertFace(1, activate: false, back: true, voice: false);
        AssertFace(2, activate: false, back: false, voice: false);
        AssertFace(3, activate: false, back: false, voice: true);

        var leftTrigger = BrowserGamepadInputReadingMapper.GetInputReading(
            BrowserGamepadInputReadingMapper.StandardMapping,
            CreateButtons(pressed: [6]),
            []);
        Assert.Equal([GamepadContextIntent.PageUp], GamepadContextIntentProjector.GetActiveIntents(leftTrigger));

        var rightTrigger = BrowserGamepadInputReadingMapper.GetInputReading(
            BrowserGamepadInputReadingMapper.StandardMapping,
            CreateButtons(pressed: [7]),
            []);
        Assert.Equal([GamepadContextIntent.PageDown], GamepadContextIntentProjector.GetActiveIntents(rightTrigger));
    }

    private static void AssertFace(int index, bool activate, bool back, bool voice)
    {
        var reading = BrowserGamepadInputReadingMapper.GetInputReading(
            BrowserGamepadInputReadingMapper.StandardMapping,
            CreateButtons(pressed: [index]),
            []);

        Assert.Equal(activate, reading.Activate);
        Assert.Equal(back, reading.Back);
        Assert.Equal(voice, reading.ShortcutVoiceToggle);

        var expectedNav = new List<GamepadNavigationIntent>();
        if (activate)
        {
            expectedNav.Add(GamepadNavigationIntent.Activate);
        }

        if (back)
        {
            expectedNav.Add(GamepadNavigationIntent.Back);
        }

        Assert.Equal(expectedNav, GamepadIntentProcessor.GetActiveIntents(reading).OrderBy(static x => x));
        Assert.Equal(
            voice ? [GamepadShortcutIntent.ToggleVoiceInput] : Array.Empty<GamepadShortcutIntent>(),
            GamepadShortcutIntentProjector.GetActiveShortcuts(reading));
    }

    private static BrowserGamepadButtonReading[] CreateButtons(IReadOnlyCollection<int> pressed)
    {
        var buttons = new BrowserGamepadButtonReading[16];
        for (var index = 0; index < buttons.Length; index++)
        {
            var isPressed = pressed.Contains(index);
            buttons[index] = new BrowserGamepadButtonReading(
                Pressed: isPressed,
                Value: isPressed ? 1 : 0);
        }

        return buttons;
    }
}
