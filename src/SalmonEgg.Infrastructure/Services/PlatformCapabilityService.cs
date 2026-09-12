using System;
using System.Runtime.InteropServices;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Infrastructure.Services;

public sealed class PlatformCapabilityService : IPlatformCapabilityService
{
    private readonly IPlatformRuntimeCapabilityProbe _runtimeProbe;
    private readonly Func<OSPlatform, bool> _isOSPlatform;
    private readonly IExternalUriLauncher _externalUriLauncher;

    public PlatformCapabilityService()
        : this(new PlatformRuntimeCapabilityProbe())
    {
    }

    public PlatformCapabilityService(IPlatformRuntimeCapabilityProbe runtimeProbe)
        : this(runtimeProbe, RuntimeInformation.IsOSPlatform)
    {
    }

    public PlatformCapabilityService(IPlatformRuntimeCapabilityProbe runtimeProbe, IExternalUriLauncher externalUriLauncher)
        : this(runtimeProbe, RuntimeInformation.IsOSPlatform)
    {
        _externalUriLauncher = externalUriLauncher ?? throw new ArgumentNullException(nameof(externalUriLauncher));
    }

    internal PlatformCapabilityService(
        IPlatformRuntimeCapabilityProbe runtimeProbe,
        Func<OSPlatform, bool> isOSPlatform)
    {
        _runtimeProbe = runtimeProbe ?? throw new ArgumentNullException(nameof(runtimeProbe));
        _isOSPlatform = isOSPlatform ?? throw new ArgumentNullException(nameof(isOSPlatform));
        _externalUriLauncher = UnsupportedExternalUriLauncher.Instance;
    }

    public bool SupportsLaunchOnStartup => IsWindowsDesktopProcessHost;

    public bool SupportsTray => IsWindowsDesktopProcessHost;

    public bool SupportsLanguageOverride => true;

    public bool SupportsMiniWindow => IsWindowsDesktopProcessHost;

    public bool SupportsExternalFileOpen => _runtimeProbe.HasExternalFileOpener;

    public bool SupportsLocalFileExport => _runtimeProbe.IsDesktopProcessHost;

    public bool SupportsStdioTransport => _runtimeProbe.IsDesktopProcessHost;

    public bool SupportsWebSocketRequestHeaders => !IsBrowserRuntime;

    public bool SupportsInteractiveTerminalSurface => _runtimeProbe.HasInteractiveTerminalSurface;

    public bool SupportsLocalTerminal => SupportsStdioTransport && SupportsInteractiveTerminalSurface;

    // Porta.Pty 1.0.7 reports Unix signal termination as ExitCode=0 and does not expose the signal.
    // Keep auth disabled there until upstream exposes a trustworthy normal exit result.
    // https://github.com/tomlm/Porta.Pty/blob/54684ba55148ed6bcd0c827ad7e8841a3289a466/src/Porta.Pty/Unix/PtyConnection.cs
    // The installed WinUI gate verifies consent, input, exit, reconnect, retry and cancellation.
    // Only that platform with both a local process host and an interactive surface is eligible.
    public bool SupportsTerminalAuthentication => IsWindowsDesktopProcessHost && SupportsInteractiveTerminalSurface;

    public bool SupportsGamepadInput => IsBrowserRuntime || IsWindowsDesktopProcessHost;

    public bool SupportsUrlElicitation => _externalUriLauncher.IsSupported;

    public bool SupportsCliCommandInspection => _runtimeProbe.IsDesktopProcessHost;

    public bool SupportsCliCommandLinking => _runtimeProbe.IsDesktopProcessHost && _isOSPlatform(OSPlatform.OSX);

    private bool IsWindowsDesktopProcessHost => _runtimeProbe.IsDesktopProcessHost && _isOSPlatform(OSPlatform.Windows);

    private static bool IsBrowserRuntime => OperatingSystem.IsBrowser();
}
