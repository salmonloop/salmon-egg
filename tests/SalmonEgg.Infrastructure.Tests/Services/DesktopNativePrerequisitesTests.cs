using System.Runtime.InteropServices;
using SalmonEgg.Infrastructure.Services;

namespace SalmonEgg.Infrastructure.Tests.Services;

public sealed class DesktopNativePrerequisitesTests
{
    [Fact]
    public void Initialize_OnLinux_LoadsFreetypeGlobally()
    {
        DesktopNativePrerequisites.Initialize();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            Assert.True(DesktopNativePrerequisites.IsFreetypeLoaded);
        }
    }
}
