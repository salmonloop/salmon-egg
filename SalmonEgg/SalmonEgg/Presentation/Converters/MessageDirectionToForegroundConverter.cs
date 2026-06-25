using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace SalmonEgg.Presentation.Converters
{
    /// <summary>
    /// 将消息方向转换为前景色（文本色）画刷
    /// 用户发出(True)返回 TextOnAccentFillColorPrimaryBrush
    /// AI接收(False)返回 TextFillColorPrimaryBrush
    /// </summary>
    public class MessageDirectionToForegroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool isOutgoing)
            {
                if (isOutgoing)
                {
                    if (Microsoft.UI.Xaml.Application.Current.Resources.TryGetValue("TextOnAccentFillColorPrimaryBrush", out var textOnAccent))
                    {
                        return textOnAccent;
                    }
                }
                else
                {
                    if (Microsoft.UI.Xaml.Application.Current.Resources.TryGetValue("TextFillColorPrimaryBrush", out var textPrimary))
                    {
                        return textPrimary;
                    }
                }
            }

            // Fallback
            return Microsoft.UI.Xaml.Application.Current.Resources["TextFillColorPrimaryBrush"];
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
