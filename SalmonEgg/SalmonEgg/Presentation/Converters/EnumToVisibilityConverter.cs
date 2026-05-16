using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace SalmonEgg.Presentation.Converters;

/// <summary>
/// A converter that returns Visibility.Visible if the value matches the parameter, 
/// otherwise Visibility.Collapsed.
/// </summary>
public sealed class EnumToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value == null || parameter == null)
        {
            return Visibility.Collapsed;
        }

        string checkValue = value.ToString() ?? string.Empty;
        string targetValue = parameter.ToString() ?? string.Empty;

        bool isMatch = string.Equals(checkValue, targetValue, StringComparison.OrdinalIgnoreCase);
        return isMatch ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        if (value is Visibility visibility && visibility == Visibility.Visible && parameter != null)
        {
            if (Enum.TryParse(targetType, parameter.ToString(), out var result))
            {
                return result;
            }
        }

        return DependencyProperty.UnsetValue;
    }
}
