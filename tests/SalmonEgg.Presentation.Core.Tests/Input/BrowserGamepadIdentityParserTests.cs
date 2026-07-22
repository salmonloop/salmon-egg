using SalmonEgg.Presentation.Core.Services.Input;

namespace SalmonEgg.Presentation.Core.Tests.Input;

public sealed class BrowserGamepadIdentityParserTests
{
    [Theory]
    [InlineData(
        "Xbox Wireless Controller (STANDARD GAMEPAD Vendor: 045e Product: 0b13)",
        "Xbox Wireless Controller",
        (ushort)0x045E,
        (ushort)0x0B13)]
    [InlineData(
        "Wireless Controller (STANDARD GAMEPAD Vendor: 054c Product: 0ce6)",
        "Wireless Controller",
        (ushort)0x054C,
        (ushort)0x0CE6)]
    [InlineData(
        "Pro Controller (STANDARD GAMEPAD Vendor: 057e Product: 2009)",
        "Pro Controller",
        (ushort)0x057E,
        (ushort)0x2009)]
    [InlineData(
        "DualSense Wireless Controller (STANDARD GAMEPAD Vendor: 054c Product: 0ce6)",
        "DualSense Wireless Controller",
        (ushort)0x054C,
        (ushort)0x0CE6)]
    [InlineData(
        "Xbox Wireless Controller (STANDARD GAMEPAD Vendor: 0x045e Product: 0x0b13)",
        "Xbox Wireless Controller",
        (ushort)0x045E,
        (ushort)0x0B13)]
    [InlineData(
        "045e-0b13-Xbox Wireless Controller",
        "Xbox Wireless Controller",
        (ushort)0x045E,
        (ushort)0x0B13)]
    [InlineData(
        "054c-0ce6-DualSense Wireless Controller",
        "DualSense Wireless Controller",
        (ushort)0x054C,
        (ushort)0x0CE6)]
    [InlineData(
        "057e-2009-Pro Controller",
        "Pro Controller",
        (ushort)0x057E,
        (ushort)0x2009)]
    public void Parse_KnownBrowserIdFormats_ExtractsNameAndHardwareIds(
        string gamepadId,
        string expectedName,
        ushort expectedVendor,
        ushort expectedProduct)
    {
        var identity = BrowserGamepadIdentityParser.Parse(gamepadId);

        Assert.Equal(expectedName, identity.DisplayName);
        Assert.Equal(expectedVendor, identity.HardwareVendorId);
        Assert.Equal(expectedProduct, identity.HardwareProductId);
        Assert.Equal(
            expectedVendor == 0x057E
                ? RawGameControllerFaceButtonLayout.Nintendo
                : RawGameControllerFaceButtonLayout.Standard,
            RawGameControllerFaceButtonLayoutResolver.Resolve(
                identity.DisplayName,
                identity.HardwareVendorId,
                labels: default));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_EmptyId_ReturnsEmptyIdentity(string? gamepadId)
    {
        var identity = BrowserGamepadIdentityParser.Parse(gamepadId);

        Assert.Equal(BrowserGamepadIdentity.Empty, identity);
    }

    [Fact]
    public void Parse_PlainNameWithoutHardwareIds_UsesNameOnly()
    {
        var identity = BrowserGamepadIdentityParser.Parse("Generic Gamepad");

        Assert.Equal("Generic Gamepad", identity.DisplayName);
        Assert.Null(identity.HardwareVendorId);
        Assert.Null(identity.HardwareProductId);
    }

    [Fact]
    public void Parse_ProControllerName_ResolvesNintendoLayoutWithoutVendor()
    {
        var identity = BrowserGamepadIdentityParser.Parse("Pro Controller");

        Assert.Equal("Pro Controller", identity.DisplayName);
        Assert.Equal(
            RawGameControllerFaceButtonLayout.Nintendo,
            RawGameControllerFaceButtonLayoutResolver.Resolve(
                identity.DisplayName,
                identity.HardwareVendorId ?? 0));
    }
}
