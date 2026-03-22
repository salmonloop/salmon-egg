using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace SalmonEgg.Presentation.Converters
{
    /// <summary>
    /// 将消息方向转换为背景色画刷
    /// 用户发出(True)返回 AccentFillColorDefaultBrush
    /// AI接收(False)返回 CardBackgroundFillColorDefaultBrush
    /// </summary>
    public class MessageDirectionToBackgroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool isOutgoing)
            {
                if (isOutgoing)
                {
                    if (Microsoft.UI.Xaml.Application.Current.Resources.TryGetValue("AccentFillColorDefaultBrush", out var accentBrush))
                    {
                        return accentBrush;
                    }
                }
                else
                {
                    if (Microsoft.UI.Xaml.Application.Current.Resources.TryGetValue("ControlFillColorSecondaryBrush", out var cardBrush))
                    {
                        return cardBrush;
                    }
                }
            }
            
            // Fallback
            return Microsoft.UI.Xaml.Application.Current.Resources["LayerOnAcrylicFillColorDefaultBrush"];
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
