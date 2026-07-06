#if __WASM__ || __ANDROID__ || __IOS__
using SalmonEgg.Infrastructure.Services;

namespace SalmonEgg;

internal sealed class RestrictedRuntimeCapabilityProbe : IPlatformRuntimeCapabilityProbe
{
    public bool IsDesktopProcessHost => false;

    public bool HasExternalFileOpener => false;

    public bool HasInteractiveTerminalSurface => false;

    public string? ResolveExternalFileOpener() => null;

    public bool CanLoadNativeLibrary(string libraryName) => false;
}
#endif
