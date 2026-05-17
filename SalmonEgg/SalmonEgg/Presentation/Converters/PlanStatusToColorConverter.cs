using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using SalmonEgg.Domain.Models.Plan;

namespace SalmonEgg.Presentation.Converters
{
    /// <summary>
    /// 将计划条目状态转换为对应的颜色
    /// </summary>
    public class PlanStatusToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is PlanEntryStatus status)
            {
                return status switch
                {
                    PlanEntryStatus.Pending => new SolidColorBrush(Microsoft.UI.Colors.Gray),
                    PlanEntryStatus.InProgress => new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 0, 120, 215)),
                    PlanEntryStatus.Completed => new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 0, 180, 0)),
                    _ => new SolidColorBrush(Microsoft.UI.Colors.Gray)
                };
            }
            return new SolidColorBrush(Microsoft.UI.Colors.Gray);
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
