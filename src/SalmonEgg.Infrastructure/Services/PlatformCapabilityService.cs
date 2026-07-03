using System;
using System.Runtime.InteropServices;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Infrastructure.Services;

public sealed class PlatformCapabilityService : IPlatformCapabilityService
{
    private readonly IPlatformRuntimeCapabilityProbe _runtimeProbe;

    public PlatformCapabilityService()
        : this(new PlatformRuntimeCapabilityProbe())
    {
    }

    public PlatformCapabilityService(IPlatformRuntimeCapabilityProbe runtimeProbe)
    {
        _runtimeProbe = runtimeProbe ?? throw new ArgumentNullException(nameof(runtimeProbe));
    }

    public bool SupportsLaunchOnStartup => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    public bool SupportsTray => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    public bool SupportsLanguageOverride => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    public bool SupportsMiniWindow => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    public bool SupportsExternalFileOpen => _runtimeProbe.HasExternalFileOpener;

    public bool SupportsLocalFileExport => _runtimeProbe.IsDesktopProcessHost;

    public bool SupportsStdioTransport => _runtimeProbe.IsDesktopProcessHost;

    public bool SupportsInteractiveTerminalSurface => _runtimeProbe.HasInteractiveTerminalSurface;

    public bool SupportsLocalTerminal => SupportsStdioTransport && SupportsInteractiveTerminalSurface;

    public bool SupportsGamepadInput => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
}
