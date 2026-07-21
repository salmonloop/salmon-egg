using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace SalmonEgg.Presentation.Utilities;

/// <summary>
/// Attaches a <see cref="ContentDialog"/> to the current XAML host so popup chrome
/// inherits the application theme. ContentDialog does not always inherit
/// <see cref="FrameworkElement.RequestedTheme"/> from the root tree when only
/// <see cref="UIElement.XamlRoot"/> is assigned.
/// </summary>
internal static class ContentDialogHost
{
    public static void AttachToXamlRoot(ContentDialog dialog, XamlRoot xamlRoot)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        ArgumentNullException.ThrowIfNull(xamlRoot);

        dialog.XamlRoot = xamlRoot;
        dialog.RequestedTheme = ResolveHostTheme(xamlRoot);
    }

    public static void AttachToElement(ContentDialog dialog, FrameworkElement host)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        ArgumentNullException.ThrowIfNull(host);

        if (host.XamlRoot is null)
        {
            return;
        }

        dialog.XamlRoot = host.XamlRoot;
        // Prefer the concrete host's resolved theme so settings pages match the shell.
        dialog.RequestedTheme = host.ActualTheme;
    }

    private static ElementTheme ResolveHostTheme(XamlRoot xamlRoot)
    {
        if (xamlRoot.Content is FrameworkElement content)
        {
            return content.ActualTheme;
        }

        return ElementTheme.Default;
    }
}
