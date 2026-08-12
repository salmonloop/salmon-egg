using Microsoft.UI.Xaml;

namespace SalmonEgg.Presentation.Services;

/// <summary>
/// Applies the application's shell identity (taskbar/Alt+Tab title and native window icon) to a
/// top-level window before it is activated.
/// </summary>
/// <remarks>
/// <para>
/// Both facets must be applied explicitly — no framework default covers every target:
/// <see cref="Window.Title"/> is empty unless set (so WinUI 3 shows "WinUI Desktop" in the
/// taskbar without it), and icon wiring diverges by compile path.
/// </para>
/// <para>
/// <c>HAS_UNO</c> and <c>WINDOWS</c> are mutually exclusive for this project:
/// <c>DisableImplicitUnoPackages=true</c> on the Windows TFM prevents Uno.WinUI from being
/// referenced there, so <c>HAS_UNO</c> (injected by <c>uno.winui.common.targets</c>) is never
/// defined for <c>net10.0-windows10.0.26100.0</c>. The <c>#elif</c> form is therefore correct.
/// </para>
/// <para>
/// Static class, not DI-injectable: <see cref="Apply"/> has no shared state and no dependencies.
/// Its timing contract (must run between window construction and first <c>Activate</c>) is owned
/// by each call site. An injectable wrapper would add indirection with no testing value.
/// Per coding-standards §10, this exception to the DI-service directory convention is recorded here.
/// </para>
/// </remarks>
internal static class WindowShellIdentity
{
    /// <summary>
    /// Shell-facing product name. Kept in sync with the <c>DisplayName</c> entries in
    /// Package.appxmanifest, which Windows uses for the packaged app's Start and taskbar labels.
    /// </summary>
    internal const string DisplayName = "Salmon Egg";

    /// <summary>
    /// Applies the title and native icon to <paramref name="window"/>. Must be called before the
    /// window is activated so the shell never observes the framework default.
    /// </summary>
    internal static void Apply(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        window.Title = DisplayName;

#if HAS_UNO
        // Uno heads resolve the Resizetizer-generated icon for the current platform.
        window.SetWindowIcon();
#elif WINDOWS
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Icons", "Windows", "icon.ico");
        if (File.Exists(iconPath))
        {
            window.AppWindow?.SetIcon(iconPath);
        }
#endif
    }
}
