using System.Runtime.InteropServices;
using SalmonEgg.Infrastructure.Services;
using Xunit;

namespace SalmonEgg.Infrastructure.Tests.Services;

public sealed class PlatformCapabilityServiceTests
{
    [Fact]
    public void SupportsLocalTerminal_RequiresTransportAndInteractiveSurface()
    {
        var sut = new PlatformCapabilityService();

        Assert.Equal(
            sut.SupportsStdioTransport && sut.SupportsInteractiveTerminalSurface,
            sut.SupportsLocalTerminal);
    }

    [Fact]
    public void SupportsInteractiveTerminalSurface_FollowsWebView2HostAvailability()
    {
        var sut = new PlatformCapabilityService();

        Assert.Equal(
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
            sut.SupportsInteractiveTerminalSurface);
    }

    [Fact]
    public void SupportsExternalFileOpen_FollowsDesktopProcessHostAvailability()
    {
        var sut = new PlatformCapabilityService();
        var expected = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            || RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            || RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

        Assert.Equal(expected, sut.SupportsExternalFileOpen);
    }

    [Fact]
    public void SupportsLocalFileExport_FollowsDesktopProcessHostAvailability()
    {
        var sut = new PlatformCapabilityService();

        Assert.Equal(sut.SupportsExternalFileOpen, sut.SupportsLocalFileExport);
    }

    [Fact]
    public void SupportsGamepadInput_FollowsWindowsGamingInputAvailability()
    {
        var sut = new PlatformCapabilityService();

        Assert.Equal(
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
            sut.SupportsGamepadInput);
    }
}
