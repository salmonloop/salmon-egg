using SalmonEgg.Presentation.Core.Services.Input;

namespace SalmonEgg.Presentation.Core.Tests.Input;

public sealed class RawGameControllerUnlabeledFaceIndexPolicyTests
{
    [Theory]
    [InlineData("Xbox Wireless Controller", 0x045E)]
    [InlineData("Wireless Controller", 0x054C)]
    [InlineData("DualSense Controller", 0x0000)]
    [InlineData("Dual Shock 4", 0x0000)]
    [InlineData("DS4 Wireless Controller", 0x0000)]
    [InlineData("PS5 Controller", 0x0000)]
    [InlineData("PS4 Controller", 0x0000)]
    [InlineData("Xbox Series X Controller", 0x0000)]
    [InlineData("Nintendo Switch Pro Controller", 0x057E)]
    [InlineData("Pro Controller", 0x0000)]
    [InlineData("Joy-Con Pair", 0x057E)]
    [InlineData("Joy-Con Grip", 0x0000)]
    [InlineData("Dual Joy-Con", 0x0000)]
    public void SupportsFullGamepadUnlabeledIndexFallback_ForKnownFullControllers(
        string displayName,
        ushort vendorId)
    {
        Assert.True(RawGameControllerUnlabeledFaceIndexPolicy.SupportsFullGamepadUnlabeledIndexFallback(
            displayName,
            vendorId));
        Assert.True(RawGameControllerUnlabeledFaceIndexPolicy.SupportsFallback(displayName, vendorId));
    }

    [Theory]
    [InlineData("Joy-Con (R)", 0x057E)]
    [InlineData("Joy-Con (L)", 0x0000)]
    [InlineData("JoyCon (R)", 0x057E)]
    [InlineData("Generic HID Device", 0x1234)]
    [InlineData("Wireless Controller", 0x1234)]
    [InlineData(null, 0x0000)]
    [InlineData("", 0x0000)]
    public void SupportsFullGamepadUnlabeledIndexFallback_RejectsSingleJoyConAndUnknown(
        string? displayName,
        ushort vendorId)
    {
        Assert.False(RawGameControllerUnlabeledFaceIndexPolicy.SupportsFullGamepadUnlabeledIndexFallback(
            displayName,
            vendorId));
        Assert.False(RawGameControllerUnlabeledFaceIndexPolicy.SupportsFallback(displayName, vendorId));
    }

    [Theory]
    // Xbox / Nintendo full pads: bottom/east/west/north at 0-3, digital triggers at 6/7.
    [InlineData("Xbox Wireless Controller", 0x045E, 0, true, false, false, 0, 0)]
    [InlineData("Xbox Wireless Controller", 0x045E, 1, false, true, false, 0, 0)]
    [InlineData("Xbox Wireless Controller", 0x045E, 2, false, false, false, 0, 0)]
    [InlineData("Xbox Wireless Controller", 0x045E, 3, false, false, true, 0, 0)]
    [InlineData("Pro Controller", 0x057E, 0, true, false, false, 0, 0)]
    [InlineData("Pro Controller", 0x057E, 1, false, true, false, 0, 0)]
    [InlineData("Pro Controller", 0x057E, 2, false, false, false, 0, 0)]
    [InlineData("Pro Controller", 0x057E, 3, false, false, true, 0, 0)]
    [InlineData("Xbox Wireless Controller", 0x045E, 6, false, false, false, 1, 0)]
    [InlineData("Pro Controller", 0x057E, 7, false, false, false, 0, 1)]
    public void Apply_MapsXboxAndNintendoPhysicalFaceAndTriggerIndexes(
        string displayName,
        ushort vendorId,
        int index,
        bool activate,
        bool back,
        bool voice,
        double leftTrigger,
        double rightTrigger)
    {
        var reading = RawGameControllerUnlabeledFaceIndexPolicy.Apply(
            index,
            default,
            displayName,
            vendorId);

        Assert.Equal(activate, reading.Activate);
        Assert.Equal(back, reading.Back);
        Assert.Equal(voice, reading.ShortcutVoiceToggle);
        Assert.Equal(leftTrigger, reading.LeftTrigger);
        Assert.Equal(rightTrigger, reading.RightTrigger);
    }

    [Theory]
    // DualSense / DualShock HID: Square Cross Circle Triangle at 0-3.
    [InlineData("Wireless Controller", 0x054C, 0, false, false, false, 0, 0)] // Square
    [InlineData("Wireless Controller", 0x054C, 1, true, false, false, 0, 0)] // Cross
    [InlineData("Wireless Controller", 0x054C, 2, false, true, false, 0, 0)] // Circle
    [InlineData("Wireless Controller", 0x054C, 3, false, false, true, 0, 0)] // Triangle
    [InlineData("DualSense Controller", 0x0000, 0, false, false, false, 0, 0)]
    [InlineData("DualSense Controller", 0x0000, 1, true, false, false, 0, 0)]
    [InlineData("Dual Sense Edge", 0x0000, 2, false, true, false, 0, 0)]
    [InlineData("PS5 Controller", 0x0000, 3, false, false, true, 0, 0)]
    [InlineData("DualShock 4", 0x0000, 6, false, false, false, 1, 0)]
    [InlineData("DS4 Wireless Controller", 0x0000, 7, false, false, false, 0, 1)]
    public void Apply_MapsSonyHidFaceAndTriggerIndexes(
        string displayName,
        ushort vendorId,
        int index,
        bool activate,
        bool back,
        bool voice,
        double leftTrigger,
        double rightTrigger)
    {
        var reading = RawGameControllerUnlabeledFaceIndexPolicy.Apply(
            index,
            default,
            displayName,
            vendorId);

        Assert.Equal(activate, reading.Activate);
        Assert.Equal(back, reading.Back);
        Assert.Equal(voice, reading.ShortcutVoiceToggle);
        Assert.Equal(leftTrigger, reading.LeftTrigger);
        Assert.Equal(rightTrigger, reading.RightTrigger);
    }
}
