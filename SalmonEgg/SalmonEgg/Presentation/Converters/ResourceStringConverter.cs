using System;
using Microsoft.UI.Xaml.Data;
using Windows.ApplicationModel.Resources;

namespace SalmonEgg.Presentation.Converters;

public sealed class ResourceStringConverter : IValueConverter
{
    private static readonly ResourceLoader ResourceLoader = ResourceLoader.GetForViewIndependentUse();

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var key = value as string;
        if (string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        var localized = ResourceLoader.GetString(key);
        if (string.IsNullOrWhiteSpace(localized))
        {
            localized = ResourceLoader.GetString(key.Replace('.', '/'));
        }

        return string.IsNullOrWhiteSpace(localized) ? key : localized;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
