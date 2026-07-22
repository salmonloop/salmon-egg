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

    [Fact]
    public void GetInputReading_WithUnlabeledKnownFaceIndexes_ProjectsPhysicalFaceSemantics()
    {
        var reading = RawGameControllerInputReadingMapper.GetInputReadingFromPresses(
            [
                new RawGameControllerButtonPress(0, RawGameControllerButtonLabel.None),
                new RawGameControllerButtonPress(3, RawGameControllerButtonLabel.None)
            ],
            [],
            [],
            RawGameControllerFaceButtonLayout.Standard,
            allowUnlabeledFaceIndexFallback: true,
            displayName: "Xbox Wireless Controller",
            hardwareVendorId: 0x045E);

        Assert.Equal([GamepadNavigationIntent.Activate], GamepadIntentProcessor.GetActiveIntents(reading));
        Assert.Equal([GamepadShortcutIntent.ToggleVoiceInput], GamepadShortcutIntentProjector.GetActiveShortcuts(reading));
    }

    [Fact]
    public void GetInputReading_WithUnlabeledSonyFaceIndexes_UsesCrossCircleSquareTriangleOrder()
    {
        var cross = RawGameControllerInputReadingMapper.GetInputReadingFromPresses(
            [new RawGameControllerButtonPress(1, RawGameControllerButtonLabel.None)],
            [],
            [],
            RawGameControllerFaceButtonLayout.Standard,
            allowUnlabeledFaceIndexFallback: true,
            displayName: "Wireless Controller",
            hardwareVendorId: 0x054C);
        var square = RawGameControllerInputReadingMapper.GetInputReadingFromPresses(
            [new RawGameControllerButtonPress(0, RawGameControllerButtonLabel.None)],
            [],
            [],
            RawGameControllerFaceButtonLayout.Standard,
            allowUnlabeledFaceIndexFallback: true,
            displayName: "DualSense Controller",
            hardwareVendorId: 0);

        Assert.Equal([GamepadNavigationIntent.Activate], GamepadIntentProcessor.GetActiveIntents(cross));
        Assert.Empty(GamepadIntentProcessor.GetActiveIntents(square));
        Assert.Empty(GamepadShortcutIntentProjector.GetActiveShortcuts(square));
    }

    [Fact]
    public void GetInputReading_WithUnlabeledIndexes_WithoutFallbackFlag_DoesNotInventSemantics()
    {
        var reading = RawGameControllerInputReadingMapper.GetInputReadingFromPresses(
            [new RawGameControllerButtonPress(0, RawGameControllerButtonLabel.None)],
            [],
            [],
            RawGameControllerFaceButtonLayout.Nintendo,
            allowUnlabeledFaceIndexFallback: false);

        Assert.Equal(default, reading);
    }

    [Fact]
    public void GetInputReading_PrefersExplicitLabelsOverUnlabeledIndexFallback()
    {
        var reading = RawGameControllerInputReadingMapper.GetInputReadingFromPresses(
            [new RawGameControllerButtonPress(0, RawGameControllerButtonLabel.Circle)],
            [],
            [],
            RawGameControllerFaceButtonLayout.Standard,
            allowUnlabeledFaceIndexFallback: true,
            displayName: "Xbox Wireless Controller",
            hardwareVendorId: 0x045E);

        // Index 0 would be Activate under Xbox fallback, but explicit Circle must remain Back.
        Assert.Equal([GamepadNavigationIntent.Back], GamepadIntentProcessor.GetActiveIntents(reading));
        Assert.False(reading.Activate);
    }

    [Fact]
    public void GetInputReading_WithUnlabeledTriggerIndexes_ProjectsPageContextIntents()
    {
        var reading = RawGameControllerInputReadingMapper.GetInputReadingFromPresses(
            [
                new RawGameControllerButtonPress(6, RawGameControllerButtonLabel.None),
                new RawGameControllerButtonPress(7, RawGameControllerButtonLabel.None)
            ],
            [],
            [],
            RawGameControllerFaceButtonLayout.Standard,
            allowUnlabeledFaceIndexFallback: true,
            displayName: "Xbox Wireless Controller",
            hardwareVendorId: 0x045E);

        Assert.Equal(1, reading.LeftTrigger);
        Assert.Equal(1, reading.RightTrigger);
        Assert.Equal(
            [GamepadContextIntent.PageUp, GamepadContextIntent.PageDown],
            GamepadContextIntentProjector.GetActiveIntents(reading).OrderBy(static intent => intent));
    }


    [Fact]
    public void GetInputReading_WithXboxAnalogTriggerAxes_ProjectsPageContextIntents()
    {
        var reading = RawGameControllerInputReadingMapper.GetInputReadingFromPresses(
            [],
            [],
            [0.5, 0.5, 0.5, 0.5, 1.0, 0.0],
            RawGameControllerFaceButtonLayout.Standard,
            allowUnlabeledFaceIndexFallback: true,
            displayName: "Xbox Wireless Controller",
            hardwareVendorId: 0x045E);

        Assert.Equal(1.0, reading.LeftTrigger);
        Assert.Equal(0.0, reading.RightTrigger);
        Assert.Equal([GamepadContextIntent.PageUp], GamepadContextIntentProjector.GetActiveIntents(reading));
        // Stick centers must not invent navigation while triggers are active.
        Assert.Empty(GamepadIntentProcessor.GetActiveIntents(reading));
    }

    [Fact]
    public void GetInputReading_WithSonyAnalogTriggerAxes_ProjectsUnitTravelAndPageDown()
    {
        var reading = RawGameControllerInputReadingMapper.GetInputReadingFromPresses(
            [],
            [],
            [0.5, 0.5, 0.5, 0.5, 0.0, 0.8],
            RawGameControllerFaceButtonLayout.Standard,
            allowUnlabeledFaceIndexFallback: true,
            displayName: "DualSense Wireless Controller",
            hardwareVendorId: 0x054C);

        Assert.Equal(0.0, reading.LeftTrigger);
        Assert.Equal(0.8, reading.RightTrigger);
        Assert.Equal([GamepadContextIntent.PageDown], GamepadContextIntentProjector.GetActiveIntents(reading));
    }

    [Fact]
    public void GetInputReading_WithNintendoAxes_DoesNotProjectAnalogTriggersFromSlots4And5()
    {
        var reading = RawGameControllerInputReadingMapper.GetInputReadingFromPresses(
            [],
            [],
            [0.5, 0.5, 0.5, 0.5, 1.0, 1.0],
            RawGameControllerFaceButtonLayout.Nintendo,
            allowUnlabeledFaceIndexFallback: true,
            displayName: "Pro Controller",
            hardwareVendorId: 0x057E);

        Assert.Equal(0.0, reading.LeftTrigger);
        Assert.Equal(0.0, reading.RightTrigger);
        Assert.Empty(GamepadContextIntentProjector.GetActiveIntents(reading));
    }

    [Fact]
    public void GetInputReading_AnalogTriggerAxes_DoNotOverrideHigherDigitalTrigger()
    {
        var reading = RawGameControllerInputReadingMapper.GetInputReadingFromPresses(
            [new RawGameControllerButtonPress(6, RawGameControllerButtonLabel.None)],
            [],
            [0.5, 0.5, 0.5, 0.5, 0.3, 0.0],
            RawGameControllerFaceButtonLayout.Standard,
            allowUnlabeledFaceIndexFallback: true,
            displayName: "Xbox Wireless Controller",
            hardwareVendorId: 0x045E);

        Assert.Equal(1.0, reading.LeftTrigger);
        Assert.Equal([GamepadContextIntent.PageUp], GamepadContextIntentProjector.GetActiveIntents(reading));
    }

    [Fact]
    public void GetInputReading_TriggerAxesAlone_DoNotInventThumbstickFromZeroStickSlots()
    {
        // Zero stick slots with active LT must not run bipolar normalizer (0 -> -1).
        var reading = RawGameControllerInputReadingMapper.GetInputReadingFromPresses(
            [],
            [],
            [0.0, 0.0, 0.0, 0.0, 1.0, 0.0],
            RawGameControllerFaceButtonLayout.Standard,
            allowUnlabeledFaceIndexFallback: true,
            displayName: "Xbox Wireless Controller",
            hardwareVendorId: 0x045E);

        Assert.Equal(0.0, reading.ThumbstickX);
        Assert.Equal(0.0, reading.ThumbstickY);
        Assert.Equal(1.0, reading.LeftTrigger);
        Assert.Empty(GamepadIntentProcessor.GetActiveIntents(reading));
        Assert.Equal([GamepadContextIntent.PageUp], GamepadContextIntentProjector.GetActiveIntents(reading));
    }
}
