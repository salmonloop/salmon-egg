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

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void SupportsGamepadInput_RequiresWindowsDesktopProcessHost(bool isDesktopProcessHost, bool expected)
    {
        var sut = new PlatformCapabilityService(
            new FakeRuntimeCapabilityProbe(
                isDesktopProcessHost: isDesktopProcessHost,
                hasExternalFileOpener: true,
                hasInteractiveTerminalSurface: true),
            platform => platform == OSPlatform.Windows);

        Assert.Equal(expected, sut.SupportsGamepadInput);
    }

    [Fact]
    public void WindowsDesktopCapabilities_RequireDesktopProcessHost()
    {
        var sut = new PlatformCapabilityService(
            new FakeRuntimeCapabilityProbe(
                isDesktopProcessHost: false,
                hasExternalFileOpener: false,
                hasInteractiveTerminalSurface: false),
            platform => platform == OSPlatform.Windows);

        Assert.False(sut.SupportsLaunchOnStartup);
        Assert.False(sut.SupportsTray);
        Assert.True(sut.SupportsLanguageOverride);
        Assert.False(sut.SupportsMiniWindow);
        Assert.False(sut.SupportsGamepadInput);
    }

    [Fact]
    public void WindowsDesktopCapabilities_RequireWindows()
    {
        var sut = new PlatformCapabilityService(
            new FakeRuntimeCapabilityProbe(
                isDesktopProcessHost: true,
                hasExternalFileOpener: true,
                hasInteractiveTerminalSurface: true),
            _ => false);

        Assert.False(sut.SupportsLaunchOnStartup);
        Assert.False(sut.SupportsTray);
        Assert.True(sut.SupportsLanguageOverride);
        Assert.False(sut.SupportsMiniWindow);
        Assert.False(sut.SupportsGamepadInput);
    }

    [Fact]
    public void WindowsDesktopCapabilities_AreExposedOnWindowsDesktopProcessHost()
    {
        var sut = new PlatformCapabilityService(
            new FakeRuntimeCapabilityProbe(
                isDesktopProcessHost: true,
                hasExternalFileOpener: true,
                hasInteractiveTerminalSurface: true),
            platform => platform == OSPlatform.Windows);

        Assert.True(sut.SupportsLaunchOnStartup);
        Assert.True(sut.SupportsTray);
        Assert.True(sut.SupportsLanguageOverride);
        Assert.True(sut.SupportsMiniWindow);
        Assert.True(sut.SupportsGamepadInput);
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
