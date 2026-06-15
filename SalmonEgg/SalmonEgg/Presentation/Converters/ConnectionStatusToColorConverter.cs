using System;
using Microsoft.UI.Xaml.Data;
using SalmonEgg.Domain.Models;

namespace SalmonEgg.Presentation.Converters;

/// <summary>
/// 将连接状态转换为颜色
/// Requirements: 4.2
/// </summary>
public class ConnectionStatusToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is ConnectionStatus status)
        {
            return status switch
            {
                ConnectionStatus.Connected => ThemeBrushConverter.Resolve("SystemFillColorSuccessBrush"),
                ConnectionStatus.Connecting => ThemeBrushConverter.Resolve("AccentBrush"),
                ConnectionStatus.Reconnecting => ThemeBrushConverter.Resolve("SystemFillColorCautionBrush", "AccentBrush"),
                ConnectionStatus.Error => ThemeBrushConverter.Resolve("SystemFillColorCriticalBrush"),
                _ => ThemeBrushConverter.Resolve("TextFillColorSecondaryBrush")
            };
        }

        return ThemeBrushConverter.Resolve("TextFillColorSecondaryBrush");
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
