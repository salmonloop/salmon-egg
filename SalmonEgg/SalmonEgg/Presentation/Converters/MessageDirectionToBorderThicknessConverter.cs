using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace SalmonEgg.Presentation.Converters
{
    /// <summary>
    /// 将消息方向转换为边框粗细
    /// 用户发出(True)返回 0 (无边框，颜色深不需要)
    /// AI接收(False)返回 1 (浅色模式下需要边框建立层级感)
    /// </summary>
    public class MessageDirectionToBorderThicknessConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool isOutgoing)
            {
                // AI 消息返回 1，用户消息返回 0
                return isOutgoing ? new Thickness(0) : new Thickness(1);
            }
            return new Thickness(0);
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
