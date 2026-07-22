using SalmonEgg.Presentation.Core.Services.Input;

namespace SalmonEgg.Presentation.Core.Tests.Input;

public sealed class RawGameControllerUnlabeledFaceIndexPolicyTests
{
    [Theory]
    [InlineData("Xbox Wireless Controller", 0x045E)]
    [InlineData("Wireless Controller", 0x054C)]
    [InlineData("DualSense Controller", 0x0000)]
    [InlineData("PS5 Controller", 0x0000)]
    [InlineData("PS4 Controller", 0x0000)]
    [InlineData("Xbox Series X Controller", 0x0000)]
    [InlineData("Nintendo Switch Pro Controller", 0x057E)]
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
    [InlineData(0, true, false, false, 0, 0)]
    [InlineData(1, false, true, false, 0, 0)]
    [InlineData(2, false, false, false, 0, 0)]
    [InlineData(3, false, false, true, 0, 0)]
    [InlineData(6, false, false, false, 1, 0)]
    [InlineData(7, false, false, false, 0, 1)]
    public void Apply_MapsCommonPhysicalFaceAndTriggerIndexes(
        int index,
        bool activate,
        bool back,
        bool voice,
        double leftTrigger,
        double rightTrigger)
    {
        var reading = RawGameControllerUnlabeledFaceIndexPolicy.Apply(index, default);

        Assert.Equal(activate, reading.Activate);
        Assert.Equal(back, reading.Back);
        Assert.Equal(voice, reading.ShortcutVoiceToggle);
        Assert.Equal(leftTrigger, reading.LeftTrigger);
        Assert.Equal(rightTrigger, reading.RightTrigger);
    }
}
