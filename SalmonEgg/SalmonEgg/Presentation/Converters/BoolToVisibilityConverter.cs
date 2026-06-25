using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace SalmonEgg.Presentation.Converters
{
    /// <summary>
    /// 将布尔值转换为 Visibility
    /// Requirements: 4.2
    /// </summary>
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool boolValue)
            {
                bool invert = parameter?.ToString() == "Invert";
                if (invert) boolValue = !boolValue;
                return boolValue ? Visibility.Visible : Visibility.Collapsed;
            }

            if (value is string stringValue)
            {
                return string.IsNullOrWhiteSpace(stringValue) ? Visibility.Collapsed : Visibility.Visible;
            }

            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            if (value is Visibility visibility)
            {
                var result = visibility == Visibility.Visible;
                var invert = parameter?.ToString() == "Invert";
                return invert ? !result : result;
            }

            return false;
        }
    }
}
