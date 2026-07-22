using SalmonEgg.Presentation.Core.Services.Input;

namespace SalmonEgg.Presentation.Core.Tests.Input;

public sealed class RawGameControllerFaceButtonLayoutResolverTests
{
    [Theory]
    [InlineData("Nintendo Switch Pro Controller", 0x0000)]
    [InlineData("Joy-Con (L)", 0x0000)]
    [InlineData("JoyCon Pair", 0x0000)]
    [InlineData("Wireless Controller", 0x057E)]
    public void Resolve_WhenNintendoIdentityIsPresent_UsesNintendoLayout(
        string displayName,
        ushort hardwareVendorId)
    {
        var layout = RawGameControllerFaceButtonLayoutResolver.Resolve(
            displayName,
            hardwareVendorId);

        Assert.Equal(RawGameControllerFaceButtonLayout.Nintendo, layout);
    }

    [Theory]
    [InlineData(null, 0x0000)]
    [InlineData("", 0x0000)]
    [InlineData("Xbox Wireless Controller", 0x045E)]
    [InlineData("Wireless Controller", 0x054C)]
    public void Resolve_WhenNintendoIdentityIsAbsent_UsesStandardLayout(
        string? displayName,
        ushort hardwareVendorId)
    {
        var layout = RawGameControllerFaceButtonLayoutResolver.Resolve(
            displayName,
            hardwareVendorId);

        Assert.Equal(RawGameControllerFaceButtonLayout.Standard, layout);
    }
}
