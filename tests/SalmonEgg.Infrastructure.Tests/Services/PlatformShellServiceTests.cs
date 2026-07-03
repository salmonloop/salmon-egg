using System.Runtime.InteropServices;
using SalmonEgg.Infrastructure.Services;
using Xunit;

namespace SalmonEgg.Infrastructure.Tests.Services;

public sealed class PlatformShellServiceTests
{
    [Fact]
    public void CreateLaunchProcessStartInfo_OnDesktopPlatforms_PassesTargetAsArgument()
    {
        var target = "folder;touch injected";

        var startInfo = PlatformShellService.CreateLaunchProcessStartInfo(
            target,
            new FakeRuntimeCapabilityProbe("xdg-open"));

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Equal(target, startInfo.FileName);
            Assert.True(startInfo.UseShellExecute);
            Assert.Empty(startInfo.ArgumentList);
            return;
        }

        Assert.False(startInfo.UseShellExecute);
        Assert.Equal(RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "open" : "xdg-open", startInfo.FileName);
        Assert.Single(startInfo.ArgumentList);
        Assert.Equal(target, startInfo.ArgumentList[0]);
    }

    [Fact]
    public void CreateLaunchProcessStartInfo_OnUnixPlatforms_DoesNotPassLeadingDashAsOption()
    {
        var target = "--help";

        var startInfo = PlatformShellService.CreateLaunchProcessStartInfo(
            target,
            new FakeRuntimeCapabilityProbe("xdg-open"));

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Equal(target, startInfo.FileName);
            Assert.True(startInfo.UseShellExecute);
            Assert.Empty(startInfo.ArgumentList);
            return;
        }

        Assert.False(startInfo.UseShellExecute);
        Assert.Equal(RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "open" : "xdg-open", startInfo.FileName);
        Assert.Single(startInfo.ArgumentList);
        Assert.Equal("./--help", startInfo.ArgumentList[0]);
    }

    [Fact]
    public void CreateLaunchProcessStartInfo_OnUnixPlatforms_SupportsGioOpen()
    {
        var target = "/tmp/salmon";

        var startInfo = PlatformShellService.CreateLaunchProcessStartInfo(
            target,
            new FakeRuntimeCapabilityProbe("gio"));

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Equal(target, startInfo.FileName);
            Assert.True(startInfo.UseShellExecute);
            Assert.Empty(startInfo.ArgumentList);
            return;
        }

        Assert.False(startInfo.UseShellExecute);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            Assert.Equal("open", startInfo.FileName);
            Assert.Single(startInfo.ArgumentList);
            Assert.Equal(target, startInfo.ArgumentList[0]);
            return;
        }

        Assert.Equal("gio", startInfo.FileName);
        Assert.Equal(["open", target], startInfo.ArgumentList);
    }

    private sealed class FakeRuntimeCapabilityProbe : IPlatformRuntimeCapabilityProbe
    {
        private readonly string _opener;

        public FakeRuntimeCapabilityProbe(string opener)
        {
            _opener = opener;
        }

        public bool IsDesktopProcessHost => true;

        public bool HasExternalFileOpener => true;

        public bool HasInteractiveTerminalSurface => true;

        public string? ResolveExternalFileOpener()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return "open";
            }

            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? string.Empty : _opener;
        }

        public bool CanLoadNativeLibrary(string libraryName) => true;
    }
}
