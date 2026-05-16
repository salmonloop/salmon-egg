using System;
using Microsoft.UI.Xaml.Data;
using SalmonEgg.Presentation.ViewModels.Settings;
using Windows.ApplicationModel.Resources;

namespace SalmonEgg.Presentation.Converters;

public sealed class HydrationCompletionModeLocalizationConverter : IValueConverter
{
    private static readonly ResourceLoader ResourceLoader = ResourceLoader.GetForViewIndependentUse();

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var mode = value switch
        {
            HydrationCompletionModeOptionViewModel option => option.Value,
            string text => text,
            _ => string.Empty
        };

        if (string.IsNullOrWhiteSpace(mode))
        {
            return string.Empty;
        }

        var isDescription = string.Equals(parameter?.ToString(), "Description", StringComparison.OrdinalIgnoreCase);
        var key = mode switch
        {
            "StrictReplay" => isDescription
                ? "Acp_HydrationMode_StrictReplay.Description"
                : "Acp_HydrationMode_StrictReplay.Name",
            "LoadResponse" => isDescription
                ? "Acp_HydrationMode_LoadResponse.Description"
                : "Acp_HydrationMode_LoadResponse.Name",
            _ => string.Empty
        };

        if (string.IsNullOrWhiteSpace(key))
        {
            return mode;
        }

        var localized = ResourceLoader.GetString(key);
        return string.IsNullOrWhiteSpace(localized) ? mode : localized;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
