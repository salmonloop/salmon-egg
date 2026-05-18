namespace SalmonEgg.Presentation.Converters;

internal static class ThemeBrushConverter
{
    public static object Resolve(string resourceKey, string fallbackResourceKey = "TextFillColorSecondaryBrush")
    {
        var resources = Microsoft.UI.Xaml.Application.Current.Resources;
        if (resources.TryGetValue(resourceKey, out var brush))
        {
            return brush;
        }

        return resources[fallbackResourceKey];
    }
}
