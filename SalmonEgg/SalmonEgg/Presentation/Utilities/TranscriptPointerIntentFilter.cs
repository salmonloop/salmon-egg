using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace SalmonEgg.Presentation.Utilities;

internal static class TranscriptPointerIntentFilter
{
    public static bool ShouldTrackViewportIntent(object? originalSource, DependencyObject boundary)
        => TranscriptPointerIntentPolicy.ShouldTrackViewportIntent(ResolveSourceKind(originalSource, boundary));

    private static TranscriptPointerSourceKind ResolveSourceKind(object? originalSource, DependencyObject boundary)
    {
        var current = originalSource as DependencyObject;
        while (current is not null && !ReferenceEquals(current, boundary))
        {
            if (current is ButtonBase
                or TextBox
                or PasswordBox
                or RichEditBox)
            {
                return TranscriptPointerSourceKind.InteractiveChild;
            }

            if (current is TextBlock { IsTextSelectionEnabled: true })
            {
                return TranscriptPointerSourceKind.SelectableText;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return TranscriptPointerSourceKind.TranscriptSurface;
    }
}
