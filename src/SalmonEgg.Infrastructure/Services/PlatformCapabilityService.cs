using System;
using System.Runtime.InteropServices;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Infrastructure.Services;

public sealed class PlatformCapabilityService : IPlatformCapabilityService
{
    private readonly IPlatformRuntimeCapabilityProbe _runtimeProbe;
    private readonly Func<OSPlatform, bool> _isOSPlatform;

    public PlatformCapabilityService()
        : this(new PlatformRuntimeCapabilityProbe())
    {
    }

    public PlatformCapabilityService(IPlatformRuntimeCapabilityProbe runtimeProbe)
        : this(runtimeProbe, RuntimeInformation.IsOSPlatform)
    {
    }

    internal PlatformCapabilityService(
        IPlatformRuntimeCapabilityProbe runtimeProbe,
        Func<OSPlatform, bool> isOSPlatform)
    {
        _runtimeProbe = runtimeProbe ?? throw new ArgumentNullException(nameof(runtimeProbe));
        _isOSPlatform = isOSPlatform ?? throw new ArgumentNullException(nameof(isOSPlatform));
    }

    public bool SupportsLaunchOnStartup => IsWindowsDesktopProcessHost;

    public bool SupportsTray => IsWindowsDesktopProcessHost;

    public bool SupportsLanguageOverride => IsWindowsDesktopProcessHost;

    public bool SupportsMiniWindow => IsWindowsDesktopProcessHost;

    public bool SupportsExternalFileOpen => _runtimeProbe.HasExternalFileOpener;

    public bool SupportsLocalFileExport => _runtimeProbe.IsDesktopProcessHost;

    public bool SupportsStdioTransport => _runtimeProbe.IsDesktopProcessHost;

    public bool SupportsInteractiveTerminalSurface => _runtimeProbe.HasInteractiveTerminalSurface;

    public bool SupportsLocalTerminal => SupportsStdioTransport && SupportsInteractiveTerminalSurface;

    public bool SupportsGamepadInput => IsBrowserRuntime || IsWindowsDesktopProcessHost;

    private bool IsWindowsDesktopProcessHost => _runtimeProbe.IsDesktopProcessHost && _isOSPlatform(OSPlatform.Windows);

    private static bool IsBrowserRuntime
    {
        get
        {
#if NET5_0_OR_GREATER
            return OperatingSystem.IsBrowser();
#else
            return false;
#endif
        }
    }
}
