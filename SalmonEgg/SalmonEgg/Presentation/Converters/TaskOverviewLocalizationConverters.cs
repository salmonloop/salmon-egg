using System;
using Microsoft.UI.Xaml.Data;
using SalmonEgg.Domain.Models.Plan;
using SalmonEgg.Presentation.Core.ViewModels.Chat.TaskOverview;
using Windows.ApplicationModel.Resources;

namespace SalmonEgg.Presentation.Converters;

public sealed class PlanStatusLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is PlanEntryStatus status
            ? TaskOverviewResourceLabels.Get(status switch
            {
                PlanEntryStatus.Pending => "TaskOverviewPlanStatusPending.Text",
                PlanEntryStatus.InProgress => "TaskOverviewPlanStatusInProgress.Text",
                PlanEntryStatus.Completed => "TaskOverviewPlanStatusCompleted.Text",
                _ => "TaskOverviewPlanStatusUnknown.Text"
            })
            : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}

public sealed class PlanPriorityLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is PlanEntryPriority priority
            ? TaskOverviewResourceLabels.Get(priority switch
            {
                PlanEntryPriority.Low => "TaskOverviewPlanPriorityLow.Text",
                PlanEntryPriority.Medium => "TaskOverviewPlanPriorityMedium.Text",
                PlanEntryPriority.High => "TaskOverviewPlanPriorityHigh.Text",
                _ => "TaskOverviewPlanPriorityUnknown.Text"
            })
            : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}

public sealed class TaskOverviewChangeKindLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is TaskOverviewChangeKind kind
            ? TaskOverviewResourceLabels.Get(kind switch
            {
                TaskOverviewChangeKind.Added => "TaskOverviewChangeKindAdded.Text",
                TaskOverviewChangeKind.Modified => "TaskOverviewChangeKindModified.Text",
                _ => "TaskOverviewChangeKindChanged.Text"
            })
            : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}

internal static class TaskOverviewResourceLabels
{
    private static readonly ResourceLoader ResourceLoader = ResourceLoader.GetForViewIndependentUse();

    public static string Get(string key)
    {
        var value = ResourceLoader.GetString(key);
        if (string.IsNullOrWhiteSpace(value))
        {
            value = ResourceLoader.GetString(key.Replace('.', '/'));
        }

        return string.IsNullOrWhiteSpace(value) ? key : value;
    }
}
