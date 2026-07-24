using System;
using Microsoft.UI.Xaml.Data;
using SalmonEgg.Acp.Plan;

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
            // PlanEntryStatus is an extensible value type (not a compile-time constant), so it
            // is matched with equality against its named members rather than a switch pattern.
            if (status == PlanEntryStatus.InProgress)
            {
                return ThemeBrushConverter.Resolve("AccentBrush");
            }

            if (status == PlanEntryStatus.Completed)
            {
                return ThemeBrushConverter.Resolve("SystemFillColorSuccessBrush");
            }
        }

        return ThemeBrushConverter.Resolve("TextFillColorSecondaryBrush");
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
