using SalmonEgg.Presentation.Core.Services.Input;

namespace SalmonEgg.Presentation.Core.Tests.Input;

public sealed class GamepadControllerIdentityTests
{
    [Theory]
    [InlineData("Xbox Wireless Controller", (ushort)0x0000)]
    [InlineData("Xbox Series X Controller", (ushort)0x0000)]
    [InlineData("Generic Pad", (ushort)0x045E)]
    public void IsXbox_WhenXboxIdentityPresent_ReturnsTrue(string displayName, ushort vendorId)
    {
        Assert.True(GamepadControllerIdentity.IsXbox(displayName, vendorId));
    }

    [Theory]
    [InlineData("Wireless Controller", (ushort)0x054C)]
    [InlineData("Pro Controller", (ushort)0x0000)]
    [InlineData(null, (ushort)0x0000)]
    public void IsXbox_WhenXboxIdentityAbsent_ReturnsFalse(string? displayName, ushort vendorId)
    {
        Assert.False(GamepadControllerIdentity.IsXbox(displayName, vendorId));
    }

    [Theory]
    [InlineData("Wireless Controller", (ushort)0x054C)]
    [InlineData("DualSense Wireless Controller", (ushort)0x0000)]
    [InlineData("DualSense Controller", (ushort)0x0000)]
    [InlineData("Dual Shock 4 Wireless Controller", (ushort)0x0000)]
    [InlineData("Dual Sense Edge", (ushort)0x0000)]
    [InlineData("PS5 Controller", (ushort)0x0000)]
    [InlineData("PS4 Controller", (ushort)0x0000)]
    [InlineData("DS4 Wireless Controller", (ushort)0x0000)]
    [InlineData("DS5 Controller", (ushort)0x0000)]
    [InlineData("PlayStation Controller", (ushort)0x0000)]
    public void IsSony_WhenSonyIdentityPresent_ReturnsTrue(string displayName, ushort vendorId)
    {
        Assert.True(GamepadControllerIdentity.IsSony(displayName, vendorId));
    }

    [Theory]
    [InlineData("Xbox Wireless Controller", (ushort)0x045E)]
    [InlineData("Pro Controller", (ushort)0x0000)]
    [InlineData("Wireless Controller", (ushort)0x0000)]
    [InlineData(null, (ushort)0x0000)]
    public void IsSony_WhenSonyIdentityAbsent_ReturnsFalse(string? displayName, ushort vendorId)
    {
        Assert.False(GamepadControllerIdentity.IsSony(displayName, vendorId));
    }

    [Theory]
    [InlineData("Nintendo Switch Pro Controller", (ushort)0x0000)]
    [InlineData("Pro Controller", (ushort)0x0000)]
    [InlineData("Wireless Controller", (ushort)0x057E)]
    [InlineData("Joy-Con (L)", (ushort)0x0000)]
    [InlineData("JoyCon Pair", (ushort)0x0000)]
    public void IsNintendo_WhenNintendoIdentityPresent_ReturnsTrue(string displayName, ushort vendorId)
    {
        Assert.True(GamepadControllerIdentity.IsNintendo(displayName, vendorId));
    }

    [Theory]
    [InlineData("Xbox Wireless Controller", (ushort)0x045E)]
    [InlineData("Wireless Controller", (ushort)0x054C)]
    [InlineData("Xbox Pro Controller", (ushort)0x0000)]
    [InlineData(null, (ushort)0x0000)]
    [InlineData("", (ushort)0x0000)]
    public void IsNintendo_WhenNintendoIdentityAbsent_ReturnsFalse(string? displayName, ushort vendorId)
    {
        Assert.False(GamepadControllerIdentity.IsNintendo(displayName, vendorId));
    }

    [Theory]
    [InlineData("PS5 Controller", (ushort)0x0000)]
    [InlineData("PS4 Controller", (ushort)0x0000)]
    [InlineData("DualSense Controller", (ushort)0x0000)]
    [InlineData("Dual Shock 4", (ushort)0x0000)]
    [InlineData("DS4 Wireless Controller", (ushort)0x0000)]
    [InlineData("Xbox Series X Controller", (ushort)0x0000)]
    [InlineData("Pro Controller", (ushort)0x0000)]
    [InlineData("Joy-Con Pair", (ushort)0x057E)]
    [InlineData("Wireless Controller", (ushort)0x054C)]
    [InlineData("Generic Pad", (ushort)0x045E)]
    public void IsFullGamepadKnownFamily_AcceptsKnownFullControllers(string displayName, ushort vendorId)
    {
        Assert.True(GamepadControllerIdentity.IsFullGamepadKnownFamily(displayName, vendorId));
    }

    [Theory]
    [InlineData("Joy-Con (L)", (ushort)0x057E)]
    [InlineData("Joy-Con (R)", (ushort)0x057E)]
    [InlineData("JoyCon (L)", (ushort)0x0000)]
    [InlineData("Generic HID Device", (ushort)0x1234)]
    [InlineData("Wireless Controller", (ushort)0x0000)]
    [InlineData(null, (ushort)0x0000)]
    public void IsFullGamepadKnownFamily_RejectsSingleJoyConAndUnknown(string? displayName, ushort vendorId)
    {
        Assert.False(GamepadControllerIdentity.IsFullGamepadKnownFamily(displayName, vendorId));
    }

    [Theory]
    [InlineData("Xbox Wireless Controller", (ushort)0x045E, false, true, false)]
    [InlineData("DualSense Wireless Controller", (ushort)0x054C, false, false, true)]
    [InlineData("Pro Controller", (ushort)0x057E, true, false, false)]
    public void FamilyHelpers_AreMutuallyExclusiveForCanonicalControllers(
        string displayName,
        ushort vendorId,
        bool nintendo,
        bool xbox,
        bool sony)
    {
        Assert.Equal(nintendo, GamepadControllerIdentity.IsNintendo(displayName, vendorId));
        Assert.Equal(xbox, GamepadControllerIdentity.IsXbox(displayName, vendorId));
        Assert.Equal(sony, GamepadControllerIdentity.IsSony(displayName, vendorId));
    }

    [Theory]
    [InlineData("Xbox Wireless Controller", (ushort)0x045E, GamepadControllerFamily.Xbox)]
    [InlineData("Generic Pad", (ushort)0x045E, GamepadControllerFamily.Xbox)]
    [InlineData("DualSense Wireless Controller", (ushort)0x054C, GamepadControllerFamily.Sony)]
    [InlineData("Wireless Controller", (ushort)0x054C, GamepadControllerFamily.Sony)]
    [InlineData("PS5 Controller", (ushort)0x0000, GamepadControllerFamily.Sony)]
    [InlineData("Pro Controller", (ushort)0x057E, GamepadControllerFamily.Nintendo)]
    [InlineData("Nintendo Switch Pro Controller", (ushort)0x0000, GamepadControllerFamily.Nintendo)]
    [InlineData("Joy-Con (L)", (ushort)0x057E, GamepadControllerFamily.Nintendo)]
    [InlineData("Generic HID Device", (ushort)0x1234, GamepadControllerFamily.Unknown)]
    [InlineData(null, (ushort)0x0000, GamepadControllerFamily.Unknown)]
    public void ResolveFamily_ProjectsAuthoritativeFamilyToken(
        string? displayName,
        ushort vendorId,
        GamepadControllerFamily expected)
    {
        Assert.Equal(expected, GamepadControllerIdentity.ResolveFamily(displayName, vendorId));
        Assert.Equal(expected, GamepadControllerIdentity.ResolveFamily(displayName, (ushort?)vendorId));
    }

    [Fact]
    public void ResolveFamily_PrefersIdentityOverFaceButtonLabels()
    {
        var labels = new StandardGamepadFaceButtonLabels(
            A: RawGameControllerButtonLabel.Cross,
            B: RawGameControllerButtonLabel.Circle,
            X: RawGameControllerButtonLabel.Square,
            Y: RawGameControllerButtonLabel.Triangle);

        // Xbox VID must win even if face labels look Sony (host remapping edge cases).
        Assert.Equal(
            GamepadControllerFamily.Xbox,
            GamepadControllerIdentity.ResolveFamily(
                displayName: "Xbox Wireless Controller",
                hardwareVendorId: 0x045E,
                faceButtonLabels: labels));
    }

    [Theory]
    [InlineData(RawGameControllerButtonLabel.Cross, RawGameControllerButtonLabel.Circle, GamepadControllerFamily.Sony)]
    [InlineData(RawGameControllerButtonLabel.LetterB, RawGameControllerButtonLabel.LetterA, GamepadControllerFamily.Nintendo)]
    [InlineData(RawGameControllerButtonLabel.XboxA, RawGameControllerButtonLabel.XboxB, GamepadControllerFamily.Xbox)]
    public void ResolveFamilyFromLabels_InfersFamilyFromHomogeneousFaceGlyphs(
        RawGameControllerButtonLabel first,
        RawGameControllerButtonLabel second,
        GamepadControllerFamily expected)
    {
        Assert.Equal(
            expected,
            GamepadControllerIdentity.ResolveFamilyFromLabels(first, second));
    }

    [Fact]
    public void ResolveFamilyFromLabels_WithMixedGlyphFamilies_ReturnsUnknown()
    {
        Assert.Equal(
            GamepadControllerFamily.Unknown,
            GamepadControllerIdentity.ResolveFamilyFromLabels(
                RawGameControllerButtonLabel.Cross,
                RawGameControllerButtonLabel.LetterB));
    }

    [Fact]
    public void ResolveFamily_WhenIdentityMissing_UsesFaceButtonLabels()
    {
        var sonyLabels = new StandardGamepadFaceButtonLabels(
            A: RawGameControllerButtonLabel.Cross,
            B: RawGameControllerButtonLabel.Circle,
            X: RawGameControllerButtonLabel.Square,
            Y: RawGameControllerButtonLabel.Triangle);
        var nintendoLabels = new StandardGamepadFaceButtonLabels(
            A: RawGameControllerButtonLabel.LetterB,
            B: RawGameControllerButtonLabel.LetterA,
            X: RawGameControllerButtonLabel.LetterY,
            Y: RawGameControllerButtonLabel.LetterX);

        Assert.Equal(
            GamepadControllerFamily.Sony,
            GamepadControllerIdentity.ResolveFamily(
                displayName: null,
                hardwareVendorId: null,
                faceButtonLabels: sonyLabels));
        Assert.Equal(
            GamepadControllerFamily.Nintendo,
            GamepadControllerIdentity.ResolveFamily(
                displayName: "Generic Pad",
                hardwareVendorId: 0,
                faceButtonLabels: nintendoLabels));
    }
    [Theory]
    [InlineData(GamepadControllerFamily.Xbox, "Xbox")]
    [InlineData(GamepadControllerFamily.Sony, "Sony")]
    [InlineData(GamepadControllerFamily.Nintendo, "Nintendo")]
    [InlineData(GamepadControllerFamily.Unknown, "Unknown")]
    public void FormatFamilyToken_ProjectsInvariantTokens(GamepadControllerFamily family, string expected)
    {
        Assert.Equal(expected, GamepadControllerIdentity.FormatFamilyToken(family));
    }

    [Theory]
    [InlineData("Xbox Wireless Controller", 0x045E, "Xbox")]
    [InlineData("DualSense Wireless Controller", 0x054C, "Sony")]
    [InlineData("Nintendo Switch Pro Controller", 0x057E, "Nintendo")]
    [InlineData("Generic Pad", 0, "Unknown")]
    public void FormatFamilyToken_MatchesResolveFamilyProjection(
        string displayName,
        int vendorId,
        string expectedToken)
    {
        var family = GamepadControllerIdentity.ResolveFamily(displayName, (ushort)vendorId);
        Assert.Equal(expectedToken, GamepadControllerIdentity.FormatFamilyToken(family));
    }

}

