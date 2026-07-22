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

    [Fact]
    public void GetInputReading_WithXboxLabels_UsesPhysicalLabelSemanticsOnStandardSlots()
    {
        var labels = new StandardGamepadFaceButtonLabels(
            A: RawGameControllerButtonLabel.XboxA,
            B: RawGameControllerButtonLabel.XboxB,
            X: RawGameControllerButtonLabel.XboxX,
            Y: RawGameControllerButtonLabel.XboxY);

        var bottom = MapFace(faceA: true, labels: labels);
        var east = MapFace(faceB: true, labels: labels);
        var west = MapFace(faceX: true, labels: labels);
        var north = MapFace(faceY: true, labels: labels);

        Assert.Equal([GamepadNavigationIntent.Activate], GamepadIntentProcessor.GetActiveIntents(bottom));
        Assert.Equal([GamepadNavigationIntent.Back], GamepadIntentProcessor.GetActiveIntents(east));
        Assert.Empty(GamepadIntentProcessor.GetActiveIntents(west));
        Assert.Empty(GamepadShortcutIntentProjector.GetActiveShortcuts(west));
        Assert.Equal([GamepadShortcutIntent.ToggleVoiceInput], GamepadShortcutIntentProjector.GetActiveShortcuts(north));
    }

    [Fact]
    public void GetInputReading_WithPlayStationLabels_UsesPhysicalLabelSemantics()
    {
        var labels = new StandardGamepadFaceButtonLabels(
            A: RawGameControllerButtonLabel.Cross,
            B: RawGameControllerButtonLabel.Circle,
            X: RawGameControllerButtonLabel.Square,
            Y: RawGameControllerButtonLabel.Triangle);

        var cross = MapFace(faceA: true, labels: labels);
        var circle = MapFace(faceB: true, labels: labels);
        var square = MapFace(faceX: true, labels: labels);
        var triangle = MapFace(faceY: true, labels: labels);

        Assert.Equal([GamepadNavigationIntent.Activate], GamepadIntentProcessor.GetActiveIntents(cross));
        Assert.Equal([GamepadNavigationIntent.Back], GamepadIntentProcessor.GetActiveIntents(circle));
        Assert.Empty(GamepadIntentProcessor.GetActiveIntents(square));
        Assert.Empty(GamepadShortcutIntentProjector.GetActiveShortcuts(square));
        Assert.Equal([GamepadShortcutIntent.ToggleVoiceInput], GamepadShortcutIntentProjector.GetActiveShortcuts(triangle));
    }

    [Fact]
    public void GetInputReading_WithNintendoLetterLabels_MapsByPhysicalGlyphNotXboxSlot()
    {
        // When Windows reports physical printed letters on standard Gamepad slots,
        // Letter* must use Nintendo physical-position semantics, not Xbox slot defaults.
        var labels = new StandardGamepadFaceButtonLabels(
            A: RawGameControllerButtonLabel.LetterB,
            B: RawGameControllerButtonLabel.LetterA,
            X: RawGameControllerButtonLabel.LetterY,
            Y: RawGameControllerButtonLabel.LetterX);

        var physicalBottom = MapFace(faceA: true, labels: labels); // slot A carries LetterB
        var physicalEast = MapFace(faceB: true, labels: labels);   // slot B carries LetterA
        var physicalWest = MapFace(faceX: true, labels: labels);   // slot X carries LetterY
        var physicalNorth = MapFace(faceY: true, labels: labels);  // slot Y carries LetterX

        Assert.Equal([GamepadNavigationIntent.Activate], GamepadIntentProcessor.GetActiveIntents(physicalBottom));
        Assert.Equal([GamepadNavigationIntent.Back], GamepadIntentProcessor.GetActiveIntents(physicalEast));
        Assert.Empty(GamepadIntentProcessor.GetActiveIntents(physicalWest));
        Assert.Empty(GamepadShortcutIntentProjector.GetActiveShortcuts(physicalWest));
        Assert.Equal([GamepadShortcutIntent.ToggleVoiceInput], GamepadShortcutIntentProjector.GetActiveShortcuts(physicalNorth));
    }

    [Fact]
    public void GetInputReading_WithMissingLabels_FallsBackToXboxSlotSemantics()
    {
        var reading = StandardGamepadInputReadingMapper.GetInputReading(
            moveUp: false,
            moveDown: false,
            moveLeft: false,
            moveRight: false,
            faceAPressed: true,
            faceBPressed: true,
            faceXPressed: true,
            faceYPressed: true,
            leftTrigger: 0,
            rightTrigger: 0,
            thumbstickX: 0,
            thumbstickY: 0,
            labels: default);

        Assert.Equal(
            [GamepadNavigationIntent.Activate, GamepadNavigationIntent.Back],
            GamepadIntentProcessor.GetActiveIntents(reading).OrderBy(static intent => intent));
        Assert.Equal([GamepadShortcutIntent.ToggleVoiceInput], GamepadShortcutIntentProjector.GetActiveShortcuts(reading));
    }

    private static GamepadInputReading MapFace(
        bool faceA = false,
        bool faceB = false,
        bool faceX = false,
        bool faceY = false,
        StandardGamepadFaceButtonLabels labels = default)
        => StandardGamepadInputReadingMapper.GetInputReading(
            moveUp: false,
            moveDown: false,
            moveLeft: false,
            moveRight: false,
            faceAPressed: faceA,
            faceBPressed: faceB,
            faceXPressed: faceX,
            faceYPressed: faceY,
            leftTrigger: 0,
            rightTrigger: 0,
            thumbstickX: 0,
            thumbstickY: 0,
            labels: labels);
}
