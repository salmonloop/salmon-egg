using SalmonEgg.Presentation.Core.Services.Input;

namespace SalmonEgg.Presentation.Core.Tests.Input;

public sealed class RawGameControllerUnlabeledFaceIndexPolicyTests
{
    [Theory]
    [InlineData("Xbox Wireless Controller", 0x045E)]
    [InlineData("Wireless Controller", 0x054C)]
    [InlineData("DualSense Controller", 0x0000)]
    [InlineData("Nintendo Switch Pro Controller", 0x057E)]
    [InlineData("Joy-Con (R)", 0x0000)]
    public void SupportsFallback_ForKnownControllerFamilies(string displayName, ushort vendorId)
    {
        Assert.True(RawGameControllerUnlabeledFaceIndexPolicy.SupportsFallback(displayName, vendorId));
    }

    [Theory]
    [InlineData("Generic HID Device", 0x1234)]
    [InlineData("Wireless Controller", 0x1234)]
    [InlineData(null, 0x0000)]
    [InlineData("", 0x0000)]
    public void SupportsFallback_RejectsUnknownControllers(string? displayName, ushort vendorId)
    {
        Assert.False(RawGameControllerUnlabeledFaceIndexPolicy.SupportsFallback(displayName, vendorId));
    }

    [Theory]
    [InlineData(0, true, false, false)]
    [InlineData(1, false, true, false)]
    [InlineData(2, false, false, false)]
    [InlineData(3, false, false, true)]
    public void Apply_MapsCommonPhysicalFaceIndexes(
        int index,
        bool activate,
        bool back,
        bool voice)
    {
        var reading = RawGameControllerUnlabeledFaceIndexPolicy.Apply(index, default);

        Assert.Equal(activate, reading.Activate);
        Assert.Equal(back, reading.Back);
        Assert.Equal(voice, reading.ShortcutVoiceToggle);
    }
}
