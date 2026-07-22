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
    [InlineData(null, GamepadControllerFamily.Xbox, "Xbox")]
    public void ResolveFamily_MapsConfirmedProfilesToInvariantTokens(
        string? profileId,
        GamepadControllerFamily expectedFamily,
        string expectedToken)
    {
        Assert.Equal(expectedFamily, GamepadHidMaestroProfileCatalog.ResolveFamily(profileId));
        Assert.Equal(expectedToken, GamepadHidMaestroProfileCatalog.FormatFamilyToken(profileId));
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
    }
}
