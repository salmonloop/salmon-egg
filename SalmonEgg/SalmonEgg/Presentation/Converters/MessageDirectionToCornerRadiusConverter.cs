using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace SalmonEgg.Presentation.Converters
{
    /// <summary>
    /// 将消息方向转换为气泡的非对称圆角
    /// 用户发出(True)为右下直角，左下圆角
    /// AI接收(False)为左下直角，右下圆角
    /// </summary>
    public class MessageDirectionToCornerRadiusConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool isOutgoing)
            {
                // 左上, 右上, 右下, 左下
                return isOutgoing ? new CornerRadius(12, 12, 0, 12) : new CornerRadius(12, 12, 12, 0);
            }
            return new CornerRadius(12);
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
