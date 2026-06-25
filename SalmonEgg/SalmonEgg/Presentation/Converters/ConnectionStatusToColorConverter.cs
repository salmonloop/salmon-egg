using System;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using SalmonEgg.Domain.Models;

namespace SalmonEgg.Presentation.Converters
{
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
                    ConnectionStatus.Connected => new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 34, 197, 94)),    // Green
                    ConnectionStatus.Connecting => new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 234, 179, 8)),   // Yellow
                    ConnectionStatus.Reconnecting => new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 249, 115, 22)),// Orange
                    ConnectionStatus.Error => new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 239, 68, 68)),        // Red
                    _ => new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 156, 163, 175))                             // Gray
                };
            }
            return new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 156, 163, 175));
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
