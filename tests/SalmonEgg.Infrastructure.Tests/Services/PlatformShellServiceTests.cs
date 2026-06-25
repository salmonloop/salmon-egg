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

        var startInfo = PlatformShellService.CreateLaunchProcessStartInfo(target);

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

        var startInfo = PlatformShellService.CreateLaunchProcessStartInfo(target);

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
}
