using System;
using Microsoft.UI.Xaml.Data;

namespace SalmonEgg.Presentation.Converters;

/// <summary>
/// 将 DateTime 转换为时间字符串格式 (HH:mm:ss)
/// </summary>
public class TimeFormatConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        // Only format when an authoritative message time is present. null/default means
        // "no time" and the bound TextBlock is also hidden via HasTimestamp; this is a
        // defense-in-depth so a visible binding still never renders a fabricated clock.
        if (value is DateTime dateTime && dateTime != default)
        {
            return dateTime.ToString("HH:mm:ss");
        }

        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
