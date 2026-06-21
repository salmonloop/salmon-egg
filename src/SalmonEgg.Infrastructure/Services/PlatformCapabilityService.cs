using System;
using System.Runtime.InteropServices;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Infrastructure.Services;

public sealed class PlatformCapabilityService : IPlatformCapabilityService
{
    public bool SupportsLaunchOnStartup => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    public bool SupportsTray => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    public bool SupportsLanguageOverride => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    public bool SupportsMiniWindow => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    public bool SupportsExternalFileOpen => IsDesktopProcessHost;

    public bool SupportsLocalFileExport => IsDesktopProcessHost;

    public bool SupportsStdioTransport => IsDesktopProcessHost;

    public bool SupportsInteractiveTerminalSurface => IsDesktopProcessHost;

    public bool SupportsLocalTerminal => SupportsStdioTransport && SupportsInteractiveTerminalSurface;

    public bool SupportsGamepadInput => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    private static bool IsDesktopProcessHost
    {
        get
        {
#if __WASM__ || __ANDROID__ || __IOS__
            return false;
#else
            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                || RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                || RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
#endif
        }
    }
}
