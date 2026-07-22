using SalmonEgg.Presentation.Core.Services.Input;

namespace SalmonEgg.Presentation.Core.Tests.Input;

public sealed class RawGameControllerFaceButtonLayoutResolverTests
{
    [Theory]
    [InlineData("Nintendo Switch Pro Controller", 0x0000)]
    [InlineData("Joy-Con (L)", 0x0000)]
    [InlineData("JoyCon Pair", 0x0000)]
    [InlineData("Pro Controller", 0x0000)]
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
    [InlineData("Xbox Pro Controller", 0x0000)]
    public void Resolve_WhenNintendoIdentityIsAbsent_UsesStandardLayout(
        string? displayName,
        ushort hardwareVendorId)
    {
        var layout = RawGameControllerFaceButtonLayoutResolver.Resolve(
            displayName,
            hardwareVendorId);

        Assert.Equal(RawGameControllerFaceButtonLayout.Standard, layout);
    }

    [Fact]
    public void Resolve_FromFaceLabels_UsesNintendoWhenAnyLetterLabelPresent()
    {
        var labels = new StandardGamepadFaceButtonLabels(
            A: RawGameControllerButtonLabel.LetterB,
            B: RawGameControllerButtonLabel.None,
            X: RawGameControllerButtonLabel.None,
            Y: RawGameControllerButtonLabel.None);

        Assert.Equal(
            RawGameControllerFaceButtonLayout.Nintendo,
            RawGameControllerFaceButtonLayoutResolver.Resolve(labels));
    }

    [Fact]
    public void Resolve_FromFaceLabels_UsesStandardWhenNoLetterLabels()
    {
        var labels = new StandardGamepadFaceButtonLabels(
            A: RawGameControllerButtonLabel.XboxA,
            B: RawGameControllerButtonLabel.Cross,
            X: RawGameControllerButtonLabel.Square,
            Y: RawGameControllerButtonLabel.Triangle);

        Assert.Equal(
            RawGameControllerFaceButtonLayout.Standard,
            RawGameControllerFaceButtonLayoutResolver.Resolve(labels));
    }

    [Fact]
    public void Resolve_FromPressedLabels_PromotesStandardIdentityToNintendoWhenLettersAppear()
    {
        var layout = RawGameControllerFaceButtonLayoutResolver.Resolve(
            RawGameControllerFaceButtonLayout.Standard,
            [RawGameControllerButtonLabel.LetterA, RawGameControllerButtonLabel.XboxA]);

        Assert.Equal(RawGameControllerFaceButtonLayout.Nintendo, layout);
    }

    [Fact]
    public void Resolve_FromPressedLabels_KeepsNintendoIdentityWithoutLetters()
    {
        var layout = RawGameControllerFaceButtonLayoutResolver.Resolve(
            RawGameControllerFaceButtonLayout.Nintendo,
            [RawGameControllerButtonLabel.Cross]);

        Assert.Equal(RawGameControllerFaceButtonLayout.Nintendo, layout);
    }

    [Fact]
    public void Resolve_WithIdentityAndLabels_PrefersNintendoIdentityEvenWithoutLetterLabels()
    {
        var labels = new StandardGamepadFaceButtonLabels(
            A: RawGameControllerButtonLabel.None,
            B: RawGameControllerButtonLabel.None,
            X: RawGameControllerButtonLabel.None,
            Y: RawGameControllerButtonLabel.None);

        var layout = RawGameControllerFaceButtonLayoutResolver.Resolve(
            "Nintendo Switch Pro Controller",
            0x057E,
            labels);

        Assert.Equal(RawGameControllerFaceButtonLayout.Nintendo, layout);
    }

    [Fact]
    public void Resolve_WithIdentityAndLabels_UsesLetterLabelsWhenIdentityIsNonNintendo()
    {
        var labels = new StandardGamepadFaceButtonLabels(
            A: RawGameControllerButtonLabel.LetterB,
            B: RawGameControllerButtonLabel.LetterA,
            X: RawGameControllerButtonLabel.LetterY,
            Y: RawGameControllerButtonLabel.LetterX);

        var layout = RawGameControllerFaceButtonLayoutResolver.Resolve(
            "Wireless Controller",
            0x054C,
            labels);

        Assert.Equal(RawGameControllerFaceButtonLayout.Nintendo, layout);
    }

    [Fact]
    public void Resolve_WithIdentityAndLabels_UsesStandardWhenNeitherIdentityNorLettersIndicateNintendo()
    {
        var labels = new StandardGamepadFaceButtonLabels(
            A: RawGameControllerButtonLabel.Cross,
            B: RawGameControllerButtonLabel.Circle,
            X: RawGameControllerButtonLabel.Square,
            Y: RawGameControllerButtonLabel.Triangle);

        var layout = RawGameControllerFaceButtonLayoutResolver.Resolve(
            "Wireless Controller",
            0x054C,
            labels);

        Assert.Equal(RawGameControllerFaceButtonLayout.Standard, layout);
    }
}
