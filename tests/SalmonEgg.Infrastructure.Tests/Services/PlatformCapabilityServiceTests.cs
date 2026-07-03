using System.Runtime.InteropServices;
using SalmonEgg.Infrastructure.Services;
using Xunit;

namespace SalmonEgg.Infrastructure.Tests.Services;

public sealed class PlatformCapabilityServiceTests
{
    [Fact]
    public void SupportsLocalTerminal_RequiresTransportAndInteractiveSurface()
    {
        var probe = new FakeRuntimeCapabilityProbe(
            isDesktopProcessHost: true,
            hasExternalFileOpener: true,
            hasInteractiveTerminalSurface: false);
        var sut = new PlatformCapabilityService(probe);

        Assert.Equal(
            sut.SupportsStdioTransport && sut.SupportsInteractiveTerminalSurface,
            sut.SupportsLocalTerminal);
        Assert.False(sut.SupportsLocalTerminal);
    }

    [Fact]
    public void SupportsInteractiveTerminalSurface_FollowsRuntimeProbe()
    {
        var sut = new PlatformCapabilityService(new FakeRuntimeCapabilityProbe(
            isDesktopProcessHost: true,
            hasExternalFileOpener: true,
            hasInteractiveTerminalSurface: false));

        Assert.False(sut.SupportsInteractiveTerminalSurface);
    }

    [Fact]
    public void SupportsExternalFileOpen_FollowsRuntimeProbe()
    {
        var sut = new PlatformCapabilityService(new FakeRuntimeCapabilityProbe(
            isDesktopProcessHost: true,
            hasExternalFileOpener: false,
            hasInteractiveTerminalSurface: true));

        Assert.False(sut.SupportsExternalFileOpen);
    }

    [Fact]
    public void SupportsLocalFileExport_FollowsDesktopProcessHostAvailability()
    {
        var sut = new PlatformCapabilityService(new FakeRuntimeCapabilityProbe(
            isDesktopProcessHost: true,
            hasExternalFileOpener: false,
            hasInteractiveTerminalSurface: false));

        Assert.True(sut.SupportsLocalFileExport);
        Assert.False(sut.SupportsExternalFileOpen);
    }

    [Fact]
    public void SupportsGamepadInput_FollowsWindowsGamingInputAvailability()
    {
        var sut = new PlatformCapabilityService();

        Assert.Equal(
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
            sut.SupportsGamepadInput);
    }

    private sealed class FakeRuntimeCapabilityProbe : IPlatformRuntimeCapabilityProbe
    {
        public FakeRuntimeCapabilityProbe(
            bool isDesktopProcessHost,
            bool hasExternalFileOpener,
            bool hasInteractiveTerminalSurface)
        {
            IsDesktopProcessHost = isDesktopProcessHost;
            HasExternalFileOpener = hasExternalFileOpener;
            HasInteractiveTerminalSurface = hasInteractiveTerminalSurface;
        }

        public bool IsDesktopProcessHost { get; }

        public bool HasExternalFileOpener { get; }

        public bool HasInteractiveTerminalSurface { get; }

        public string? ResolveExternalFileOpener() => null;

        public bool CanLoadNativeLibrary(string libraryName) => false;
    }
}
