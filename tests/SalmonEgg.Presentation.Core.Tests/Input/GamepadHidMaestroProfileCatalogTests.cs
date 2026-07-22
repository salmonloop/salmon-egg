using SalmonEgg.Presentation.Core.Services.Input;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Input;

public sealed class GamepadHidMaestroProfileCatalogTests
{
    [Theory]
    [InlineData(null, "xbox-360-wired")]
    [InlineData("", "xbox-360-wired")]
    [InlineData("  ", "xbox-360-wired")]
    [InlineData("dualsense", "dualsense")]
    [InlineData(" DualSense-BT ", "DualSense-BT")]
    public void NormalizeProfileId_DefaultsBlankAndTrims(string? profileId, string expected)
    {
        Assert.Equal(expected, GamepadHidMaestroProfileCatalog.NormalizeProfileId(profileId));
    }

    [Theory]
    [InlineData("xbox-360-wired", true)]
    [InlineData("xbox-series-xs", true)]
    [InlineData("dualsense", true)]
    [InlineData("dualsense-bt", true)]
    [InlineData("dualshock-4-v2", true)]
    [InlineData("switch-pro", true)]
    [InlineData("SWITCH-PRO", true)]
    [InlineData("not-a-real-profile", false)]
    [InlineData(null, false)]
    public void IsConfirmedProfileId_OnlyKnownCatalogEntries(string? profileId, bool expected)
    {
        Assert.Equal(expected, GamepadHidMaestroProfileCatalog.IsConfirmedProfileId(profileId));
    }

    [Theory]
    [InlineData("xbox-360-wired", GamepadControllerFamily.Xbox, "Xbox")]
    [InlineData("xbox-series-xs", GamepadControllerFamily.Xbox, "Xbox")]
    [InlineData("dualsense", GamepadControllerFamily.Sony, "Sony")]
    [InlineData("dualsense-bt", GamepadControllerFamily.Sony, "Sony")]
    [InlineData("dualshock-4-v2", GamepadControllerFamily.Sony, "Sony")]
    [InlineData("switch-pro", GamepadControllerFamily.Nintendo, "Nintendo")]
    [InlineData("SWITCH-PRO", GamepadControllerFamily.Nintendo, "Nintendo")]
    [InlineData(null, GamepadControllerFamily.Xbox, "Xbox")]
    [InlineData("", GamepadControllerFamily.Xbox, "Xbox")]
    [InlineData("  ", GamepadControllerFamily.Xbox, "Xbox")]
    public void ResolveFamily_MapsConfirmedProfilesToInvariantTokens(
        string? profileId,
        GamepadControllerFamily expectedFamily,
        string expectedToken)
    {
        Assert.Equal(expectedFamily, GamepadHidMaestroProfileCatalog.ResolveFamily(profileId));
        Assert.Equal(expectedToken, GamepadHidMaestroProfileCatalog.FormatFamilyToken(profileId));
    }

    [Theory]
    [InlineData("not-a-real-profile")]
    [InlineData("dualsense-usb")]
    [InlineData("xbox-one")]
    [InlineData("switch-joycon")]
    public void ResolveFamily_UnconfirmedNonBlankProfile_IsUnknown(string profileId)
    {
        Assert.False(GamepadHidMaestroProfileCatalog.IsConfirmedProfileId(profileId));
        Assert.Equal(GamepadControllerFamily.Unknown, GamepadHidMaestroProfileCatalog.ResolveFamily(profileId));
        Assert.Equal("Unknown", GamepadHidMaestroProfileCatalog.FormatFamilyToken(profileId));
    }

    [Fact]
    public void ConfirmedProfileIds_ContainsAllDocumentedMultiBrandProfiles()
    {
        Assert.Contains(GamepadHidMaestroProfileCatalog.Xbox360Wired, GamepadHidMaestroProfileCatalog.ConfirmedProfileIds);
        Assert.Contains(GamepadHidMaestroProfileCatalog.XboxSeriesXs, GamepadHidMaestroProfileCatalog.ConfirmedProfileIds);
        Assert.Contains(GamepadHidMaestroProfileCatalog.DualSense, GamepadHidMaestroProfileCatalog.ConfirmedProfileIds);
        Assert.Contains(GamepadHidMaestroProfileCatalog.DualSenseBluetooth, GamepadHidMaestroProfileCatalog.ConfirmedProfileIds);
        Assert.Contains(GamepadHidMaestroProfileCatalog.DualShock4V2, GamepadHidMaestroProfileCatalog.ConfirmedProfileIds);
        Assert.Contains(GamepadHidMaestroProfileCatalog.SwitchPro, GamepadHidMaestroProfileCatalog.ConfirmedProfileIds);
        Assert.Equal(6, GamepadHidMaestroProfileCatalog.ConfirmedProfileIds.Count);

        foreach (var profileId in GamepadHidMaestroProfileCatalog.ConfirmedProfileIds)
        {
            Assert.True(GamepadHidMaestroProfileCatalog.IsConfirmedProfileId(profileId));
            Assert.NotEqual(
                GamepadControllerFamily.Unknown,
                GamepadHidMaestroProfileCatalog.ResolveFamily(profileId));
            Assert.NotEqual("Unknown", GamepadHidMaestroProfileCatalog.FormatFamilyToken(profileId));
        }
    }

    public static IEnumerable<object[]> PhysicalFaceCandidateSamples()
    {
        yield return [GamepadHidMaestroProfileCatalog.Xbox360Wired, GamepadFaceSemantic.Activate, new[] { "A" }];
        yield return [GamepadHidMaestroProfileCatalog.Xbox360Wired, GamepadFaceSemantic.Back, new[] { "B" }];
        yield return [GamepadHidMaestroProfileCatalog.Xbox360Wired, GamepadFaceSemantic.West, new[] { "X" }];
        yield return [GamepadHidMaestroProfileCatalog.Xbox360Wired, GamepadFaceSemantic.Voice, new[] { "Y" }];
        yield return [GamepadHidMaestroProfileCatalog.XboxSeriesXs, GamepadFaceSemantic.Activate, new[] { "A" }];
        yield return [GamepadHidMaestroProfileCatalog.DualSense, GamepadFaceSemantic.Activate, new[] { "Cross", "A" }];
        yield return [GamepadHidMaestroProfileCatalog.DualSense, GamepadFaceSemantic.Back, new[] { "Circle", "B" }];
        yield return [GamepadHidMaestroProfileCatalog.DualSense, GamepadFaceSemantic.West, new[] { "Square", "X" }];
        yield return [GamepadHidMaestroProfileCatalog.DualSense, GamepadFaceSemantic.Voice, new[] { "Triangle", "Y" }];
        yield return [GamepadHidMaestroProfileCatalog.DualSenseBluetooth, GamepadFaceSemantic.Activate, new[] { "Cross", "A" }];
        yield return [GamepadHidMaestroProfileCatalog.DualShock4V2, GamepadFaceSemantic.Back, new[] { "Circle", "B" }];
        yield return [GamepadHidMaestroProfileCatalog.SwitchPro, GamepadFaceSemantic.Activate, new[] { "B" }];
        yield return [GamepadHidMaestroProfileCatalog.SwitchPro, GamepadFaceSemantic.Back, new[] { "A" }];
        yield return [GamepadHidMaestroProfileCatalog.SwitchPro, GamepadFaceSemantic.West, new[] { "Y" }];
        yield return [GamepadHidMaestroProfileCatalog.SwitchPro, GamepadFaceSemantic.Voice, new[] { "X" }];
        // Unconfirmed: inject fallback Xbox letters, but family remains Unknown.
        yield return ["not-a-real-profile", GamepadFaceSemantic.Activate, new[] { "A" }];
        yield return ["not-a-real-profile", GamepadFaceSemantic.West, new[] { "X" }];
    }

    [Theory]
    [MemberData(nameof(PhysicalFaceCandidateSamples))]
    public void GetPhysicalButtonNameCandidates_MapsFamilyToOrderedPhysicalKeys(
        string profileId,
        GamepadFaceSemantic semantic,
        string[] expected)
    {
        Assert.Equal(
            expected,
            GamepadHidMaestroProfileCatalog.GetPhysicalButtonNameCandidates(profileId, semantic));
    }

    [Fact]
    public void GetPhysicalButtonNameCandidates_BlankProfileUsesDefaultXboxKeys()
    {
        Assert.Equal(
            ["A"],
            GamepadHidMaestroProfileCatalog.GetPhysicalButtonNameCandidates(null, GamepadFaceSemantic.Activate));
        Assert.Equal(
            ["Y"],
            GamepadHidMaestroProfileCatalog.GetPhysicalButtonNameCandidates("  ", GamepadFaceSemantic.Voice));
    }
}
