using System;
using Microsoft.UI.Xaml.Data;
using SalmonEgg.Acp.Plan;
using SalmonEgg.Presentation.Core.ViewModels.Chat.TaskOverview;
using Windows.ApplicationModel.Resources;

namespace SalmonEgg.Presentation.Converters;

public sealed class PlanStatusLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not PlanEntryStatus status)
        {
            return string.Empty;
        }

        // PlanEntryStatus is an extensible value type (not a compile-time constant), so it
        // is matched with equality against its named members rather than a switch pattern.
        if (status == PlanEntryStatus.Pending)
        {
            return TaskOverviewResourceLabels.Get("TaskOverviewPlanStatusPending.Text");
        }

        if (status == PlanEntryStatus.InProgress)
        {
            return TaskOverviewResourceLabels.Get("TaskOverviewPlanStatusInProgress.Text");
        }

        if (status == PlanEntryStatus.Completed)
        {
            return TaskOverviewResourceLabels.Get("TaskOverviewPlanStatusCompleted.Text");
        }

        return TaskOverviewResourceLabels.Get("TaskOverviewPlanStatusUnknown.Text");
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}

public sealed class PlanPriorityLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not PlanEntryPriority priority)
        {
            return string.Empty;
        }

        // PlanEntryPriority is an extensible value type (not a compile-time constant), so it
        // is matched with equality against its named members rather than a switch pattern.
        if (priority == PlanEntryPriority.Low)
        {
            return TaskOverviewResourceLabels.Get("TaskOverviewPlanPriorityLow.Text");
        }

        if (priority == PlanEntryPriority.Medium)
        {
            return TaskOverviewResourceLabels.Get("TaskOverviewPlanPriorityMedium.Text");
        }

        if (priority == PlanEntryPriority.High)
        {
            return TaskOverviewResourceLabels.Get("TaskOverviewPlanPriorityHigh.Text");
        }

        return TaskOverviewResourceLabels.Get("TaskOverviewPlanPriorityUnknown.Text");
    }

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
