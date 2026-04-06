using System.Runtime.InteropServices;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Infrastructure.Services;

public sealed class PlatformIconService : IPlatformIconService
{
    private readonly bool _isWindows;

    public PlatformIconService()
        : this(RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
    }

    public PlatformIconService(bool isWindows)
    {
        _isWindows = isWindows;
    }

    public IconPresentation GetPresentation(IconUsage usage)
    {
        if (!_isWindows)
        {
            return new IconPresentation(prefersNativeIcon: false, supportsNativeAnimation: false);
        }

        if (usage == IconUsage.Interactive)
        {
            return new IconPresentation(prefersNativeIcon: true, supportsNativeAnimation: true);
        }

        return new IconPresentation(prefersNativeIcon: true, supportsNativeAnimation: false);
    }
}
