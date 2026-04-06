namespace SalmonEgg.Domain.Services;

public enum IconUsage
{
    Static,
    Interactive
}

public sealed class IconPresentation
{
    public IconPresentation(
        bool prefersNativeIcon,
        bool supportsNativeAnimation)
    {
        PrefersNativeIcon = prefersNativeIcon;
        SupportsNativeAnimation = supportsNativeAnimation;
    }

    public bool PrefersNativeIcon { get; }

    public bool SupportsNativeAnimation { get; }
}

public interface IPlatformIconService
{
    IconPresentation GetPresentation(IconUsage usage);
}
