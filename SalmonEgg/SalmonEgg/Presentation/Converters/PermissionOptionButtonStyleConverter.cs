using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace SalmonEgg.Presentation.Converters;

public sealed class PermissionOptionButtonStyleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool isAllow && isAllow)
        {
            return Microsoft.UI.Xaml.Application.Current.Resources["AccentButtonStyle"];
        }

        return Microsoft.UI.Xaml.Application.Current.Resources["DefaultButtonStyle"];
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
