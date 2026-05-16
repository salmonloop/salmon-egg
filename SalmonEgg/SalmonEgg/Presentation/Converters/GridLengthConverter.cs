using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace SalmonEgg.Presentation.Converters;

public sealed class GridLengthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is double d)
        {
            return new GridLength(d);
        }
        return new GridLength(0);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        if (value is GridLength gl)
        {
            return gl.Value;
        }
        return 0.0;
    }
}
