using System;
using System.Runtime.InteropServices;

namespace SalmonEgg.Infrastructure.Services;

public sealed class PlatformRuntimeCapabilityProbe : IPlatformRuntimeCapabilityProbe
{
    private const int RtldLazy = 1;
    private static readonly object Gate = new();
    private static bool? _hasExternalFileOpener;
    private static bool? _hasInteractiveTerminalSurface;

    public bool IsDesktopProcessHost
    {
        get
        {
#if __WASM__ || __ANDROID__ || __IOS__
            return false;
#else
            if (IsRestrictedRuntime())
            {
                return false;
            }

            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                || RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                || RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
#endif
        }
    }

    public bool HasExternalFileOpener
    {
        get
        {
            if (!IsDesktopProcessHost)
            {
                return false;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return true;
            }

            lock (Gate)
            {
                _hasExternalFileOpener ??= ResolveExternalFileOpener() != null;
                return _hasExternalFileOpener.Value;
            }
        }
    }

    public bool HasInteractiveTerminalSurface
    {
        get
        {
            if (!IsDesktopProcessHost)
            {
                return false;
            }

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return true;
            }

            lock (Gate)
            {
                _hasInteractiveTerminalSurface ??= HasLinuxWebViewRuntime();
                return _hasInteractiveTerminalSurface.Value;
            }
        }
    }

    public string? ResolveExternalFileOpener()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            if (RuntimeCommandResolver.TryResolve("xdg-open", out var xdgOpen))
            {
                return xdgOpen;
            }

            if (RuntimeCommandResolver.TryResolve("gio", out var gio))
            {
                return gio;
            }
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return RuntimeCommandResolver.TryResolve("open", out var open) ? open : "open";
        }

        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? string.Empty : null;
    }

    public bool CanLoadNativeLibrary(string libraryName)
    {
        if (string.IsNullOrWhiteSpace(libraryName))
        {
            return false;
        }

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return true;
        }

        var handle = Dlopen(libraryName, RtldLazy);
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        Dlclose(handle);
        return true;
    }

    private bool HasLinuxWebViewRuntime()
    {
        return CanLoadAnyNativeLibrary(
            "libwebkit2gtk-4.1.so.0",
            "libwebkit2gtk-4.0.so.37")
            && CanLoadAnyNativeLibrary(
                "libjavascriptcoregtk-4.1.so.0",
            "libjavascriptcoregtk-4.0.so.18");
    }

    private static bool IsRestrictedRuntime()
    {
#if NET5_0_OR_GREATER
        return OperatingSystem.IsBrowser()
            || OperatingSystem.IsAndroid()
            || OperatingSystem.IsIOS();
#else
        return false;
#endif
    }

    private bool CanLoadAnyNativeLibrary(params string[] libraryNames)
    {
        foreach (var libraryName in libraryNames)
        {
            if (CanLoadNativeLibrary(libraryName))
            {
                return true;
            }
        }

        return false;
    }

    [DllImport("libdl.so.2", EntryPoint = "dlopen")]
    private static extern IntPtr Dlopen(string fileName, int flags);

    [DllImport("libdl.so.2", EntryPoint = "dlclose")]
    private static extern int Dlclose(IntPtr handle);
}
