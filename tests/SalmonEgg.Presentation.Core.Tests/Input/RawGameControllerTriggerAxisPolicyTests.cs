using System;
using System.Linq;
using SalmonEgg.Presentation.Core.Services.Input;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Input;

public sealed class RawGameControllerTriggerAxisPolicyTests
{
    [Theory]
    [InlineData("Xbox Wireless Controller", (ushort)0x045E, true)]
    [InlineData("DualSense Wireless Controller", (ushort)0x054C, true)]
    [InlineData("Wireless Controller", (ushort)0x054C, true)]
    [InlineData("Pro Controller", (ushort)0x057E, false)]
    [InlineData("Nintendo Switch Pro Controller", (ushort)0, false)]
    [InlineData("Generic HID Pad", (ushort)0, false)]
    public void SupportsAnalogTriggerAxes_OnlyXboxAndSonyFullPads(
        string displayName,
        ushort hardwareVendorId,
        bool expected)
    {
        Assert.Equal(
            expected,
            RawGameControllerTriggerAxisPolicy.SupportsAnalogTriggerAxes(displayName, hardwareVendorId));
    }

    [Fact]
    public void Apply_RequiresAxes()
    {
        Assert.Throws<ArgumentNullException>(() =>
            RawGameControllerTriggerAxisPolicy.Apply(
                null!,
                default,
                "Xbox Wireless Controller",
                GamepadControllerIdentity.MicrosoftVendorId));
    }

    [Fact]
    public void Apply_WithXboxAnalogTriggers_ProjectsUnitTravelAndPageIntents()
    {
        // Arrange: stick slots idle at center (0.5), LT/RT at axes 4/5 unipolar.
        var axes = new[] { 0.5, 0.5, 0.5, 0.5, 0.75, 1.0 };

        // Act
        var reading = RawGameControllerTriggerAxisPolicy.Apply(
            axes,
            default,
            "Xbox Wireless Controller",
            GamepadControllerIdentity.MicrosoftVendorId);

        // Assert
        Assert.Equal(0.75, reading.LeftTrigger);
        Assert.Equal(1.0, reading.RightTrigger);
        Assert.Equal(
            [GamepadContextIntent.PageUp, GamepadContextIntent.PageDown],
            GamepadContextIntentProjector.GetActiveIntents(reading).OrderBy(static intent => intent));
    }

    [Fact]
    public void Apply_WithSonyAnalogTriggers_ProjectsUnitTravel()
    {
        var axes = new[] { 0.5, 0.5, 0.5, 0.5, 0.5, 0.0 };

        var reading = RawGameControllerTriggerAxisPolicy.Apply(
            axes,
            default,
            "DualSense Wireless Controller",
            GamepadControllerIdentity.SonyVendorId);

        Assert.Equal(0.5, reading.LeftTrigger);
        Assert.Equal(0.0, reading.RightTrigger);
        Assert.Equal([GamepadContextIntent.PageUp], GamepadContextIntentProjector.GetActiveIntents(reading));
    }

    [Fact]
    public void Apply_WithNintendoAxes_DoesNotInventAnalogTriggers()
    {
        var axes = new[] { 0.5, 0.5, 0.5, 0.5, 1.0, 1.0 };

        var reading = RawGameControllerTriggerAxisPolicy.Apply(
            axes,
            default,
            "Pro Controller",
            GamepadControllerIdentity.NintendoVendorId);

        Assert.Equal(default, reading);
    }

    [Fact]
    public void Apply_WithFewerThanSixAxes_DoesNotReadMissingSlots()
    {
        var axes = new[] { 0.5, 0.5, 1.0, 1.0 };

        var reading = RawGameControllerTriggerAxisPolicy.Apply(
            axes,
            default,
            "Xbox Wireless Controller",
            GamepadControllerIdentity.MicrosoftVendorId);

        Assert.Equal(default, reading);
    }

    [Fact]
    public void Apply_MergesWithExistingDigitalTriggerUsingMax()
    {
        var existing = default(GamepadInputReading) with { LeftTrigger = 1.0, RightTrigger = 0.25 };
        var axes = new[] { 0.5, 0.5, 0.5, 0.5, 0.4, 0.9 };

        var reading = RawGameControllerTriggerAxisPolicy.Apply(
            axes,
            existing,
            "Xbox Wireless Controller",
            GamepadControllerIdentity.MicrosoftVendorId);

        Assert.Equal(1.0, reading.LeftTrigger);
        Assert.Equal(0.9, reading.RightTrigger);
    }

    [Fact]
    public void Apply_TreatsNonFiniteAxisAsReleased()
    {
        var axes = new[] { 0.5, 0.5, 0.5, 0.5, double.NaN, double.PositiveInfinity };

        var reading = RawGameControllerTriggerAxisPolicy.Apply(
            axes,
            default,
            "Xbox Wireless Controller",
            GamepadControllerIdentity.MicrosoftVendorId);

        Assert.Equal(default, reading);
    }

    [Fact]
    public void Apply_WithSonyDigitalAndPartialAnalog_MergesUsingMax()
    {
        var existing = default(GamepadInputReading) with { LeftTrigger = 1.0 };
        var axes = new[] { 0.5, 0.5, 0.5, 0.5, 0.2, 0.0 };

        var reading = RawGameControllerTriggerAxisPolicy.Apply(
            axes,
            existing,
            "DualSense Wireless Controller",
            GamepadControllerIdentity.SonyVendorId);

        Assert.Equal(1.0, reading.LeftTrigger);
        Assert.Equal(0.0, reading.RightTrigger);
    }
}
