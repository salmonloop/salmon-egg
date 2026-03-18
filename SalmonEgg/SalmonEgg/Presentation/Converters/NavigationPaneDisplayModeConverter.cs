using System;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using SalmonEgg.Presentation.Core.Mvux.ShellLayout;
using SalmonEgg.Presentation.Core.ViewModels.ShellLayout;

namespace SalmonEgg.Presentation.Converters;

public sealed class NavigationPaneDisplayModeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is ShellLayoutViewModel layout)
        {
            return Convert(layout.NavPaneDisplayMode, layout.IsNavPaneOpen);
        }

        if (value is NavigationPaneDisplayMode mode)
        {
            return Convert(mode, isPaneOpen: true);
        }

        return NavigationViewPaneDisplayMode.Auto;
    }

    private static NavigationViewPaneDisplayMode Convert(NavigationPaneDisplayMode mode, bool isPaneOpen)
        => mode switch
        {
            NavigationPaneDisplayMode.Expanded => isPaneOpen
                ? NavigationViewPaneDisplayMode.Left
                : NavigationViewPaneDisplayMode.LeftCompact,
            NavigationPaneDisplayMode.Compact => NavigationViewPaneDisplayMode.LeftCompact,
            NavigationPaneDisplayMode.Minimal => NavigationViewPaneDisplayMode.LeftMinimal,
            _ => NavigationViewPaneDisplayMode.Auto
        };

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
