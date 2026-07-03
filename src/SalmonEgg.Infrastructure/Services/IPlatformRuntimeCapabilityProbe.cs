using System;

namespace SalmonEgg.Infrastructure.Services;

public interface IPlatformRuntimeCapabilityProbe
{
    bool IsDesktopProcessHost { get; }

    bool HasExternalFileOpener { get; }

    bool HasInteractiveTerminalSurface { get; }

    string? ResolveExternalFileOpener();

    bool CanLoadNativeLibrary(string libraryName);
}
