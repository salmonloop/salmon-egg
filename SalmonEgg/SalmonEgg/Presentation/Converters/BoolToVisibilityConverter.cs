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
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
