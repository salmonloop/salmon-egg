using System;
using Microsoft.UI.Xaml.Data;
using SalmonEgg.Domain.Models.Plan;

namespace SalmonEgg.Presentation.Converters;

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
                PlanEntryStatus.InProgress => ThemeBrushConverter.Resolve("AccentBrush"),
                PlanEntryStatus.Completed => ThemeBrushConverter.Resolve("SystemFillColorSuccessBrush"),
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
