using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace SalmonEgg.Presentation.Utilities;

internal static class DependencyObjectAncestry
{
    public static T? FindAncestorOrSelf<T>(DependencyObject? current)
        where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T typed)
            {
                return typed;
            }

            current = GetParent(current);
        }

        return default;
    }

    private static DependencyObject? GetParent(DependencyObject current)
    {
        if (current is FrameworkElement frameworkElement && frameworkElement.Parent is DependencyObject parent)
        {
            return parent;
        }

        return VisualTreeHelper.GetParent(current);
    }
}
