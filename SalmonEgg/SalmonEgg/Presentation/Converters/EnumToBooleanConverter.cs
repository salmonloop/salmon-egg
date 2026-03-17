using System;
using Microsoft.UI.Xaml.Data;

namespace SalmonEgg.Presentation.Converters;

/// <summary>
/// A converter that returns true if the value matches the parameter.
/// Useful for binding Visibility or IsChecked to Enum values.
/// </summary>
public sealed class EnumToBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value == null || parameter == null)
        {
            return false;
        }

        string checkValue = value?.ToString() ?? string.Empty;
        string targetValue = parameter?.ToString() ?? string.Empty;

        return string.Equals(checkValue, targetValue, StringComparison.OrdinalIgnoreCase);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        if (value is bool b && b && parameter != null)
        {
            if (Enum.TryParse(targetType, parameter.ToString(), out var result))
            {
                return result;
            }
        }

        return Microsoft.UI.Xaml.DependencyProperty.UnsetValue;
    }
}

