using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using SalmonEgg.Presentation.Core.Mvux.ShellLayout;

namespace SalmonEgg.Presentation.Converters;

public sealed class LayoutPaddingConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is LayoutPadding p)
        {
            return new Thickness(p.Left, p.Top, p.Right, p.Bottom);
        }
        return new Thickness(0);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
