using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace SalmonEgg.Presentation.Converters
{
    /// <summary>
    /// 将消息方向转换为对齐方式
    /// Requirements: 4.2
    /// </summary>
    public class MessageDirectionToAlignmentConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool isOutgoing)
            {
                return isOutgoing ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            }
            return HorizontalAlignment.Left;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
