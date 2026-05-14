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

    public bool SupportsStdioTransport => IsDesktopProcessHost;

    public bool SupportsLocalTerminal => IsDesktopProcessHost;

    private static bool IsDesktopProcessHost =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        || RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
        || RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
}
