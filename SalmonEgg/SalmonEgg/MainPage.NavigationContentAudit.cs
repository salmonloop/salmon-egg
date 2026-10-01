using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SalmonEgg.Presentation.ViewModels.Navigation;

namespace SalmonEgg;

public sealed partial class MainPage
{
#if DEBUG
    private bool _navigationContentAuditScheduled;
#endif

    [Conditional("DEBUG")]
    private void ScheduleNavigationContentAudit()
    {
#if DEBUG
        if (_navigationContentAuditScheduled
            || !string.Equals(Environment.GetEnvironmentVariable("SALMONEGG_NAV_MASK_PROBE"), "1", StringComparison.Ordinal))
        {
            return;
        }

        _navigationContentAuditScheduled = true;
        CompositionTarget.Rendering += OnNavigationContentAuditFrame;
        Unloaded += OnNavigationContentAuditUnloaded;
#endif
    }

#if DEBUG
    private void OnNavigationContentAuditFrame(object? sender, object args)
    {
        DetachNavigationContentAudit();
        var containers = new List<NavigationViewItem>();
        CollectNavigationViewItems(MainNavView, containers);
        var samples = new List<string>();
        foreach (var container in containers)
        {
            if (container.DataContext is not SessionNavItemViewModel { IsPlaceholder: false } row) continue;
            var content = container.Content as FrameworkElement;
            var visible = content is not null
                && TryGetVisibleNavigationAncestorOpacity(content, out var ancestorOpacity)
                && ContainsVisibleNavigationTitle(content, row.Title, ancestorOpacity);
            samples.Add($"{row.SessionId}:{visible}");
        }

        App.BootLog($"NavContentAudit rows={samples.Count} visibleTitles={samples.Count(sample => sample.EndsWith(":True", StringComparison.Ordinal))} sessions=[{string.Join(",", samples)}]");
    }

    private void OnNavigationContentAuditUnloaded(object sender, RoutedEventArgs args) => DetachNavigationContentAudit();

    private void DetachNavigationContentAudit()
    {
        CompositionTarget.Rendering -= OnNavigationContentAuditFrame;
        Unloaded -= OnNavigationContentAuditUnloaded;
        _navigationContentAuditScheduled = false;
    }

    private static bool ContainsVisibleNavigationTitle(FrameworkElement element, string title, double inheritedOpacity)
    {
        var opacity = inheritedOpacity * element.Opacity;
        if (element.Visibility != Visibility.Visible || element.ActualWidth <= 0 || element.ActualHeight <= 0 || opacity < 0.99)
        {
            return false;
        }

        if (element is TextBlock text && string.Equals(text.Text, title, StringComparison.Ordinal)) return true;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
        {
            if (VisualTreeHelper.GetChild(element, index) is FrameworkElement child
                && ContainsVisibleNavigationTitle(child, title, opacity)) return true;
        }

        return false;
    }

    private static bool TryGetVisibleNavigationAncestorOpacity(FrameworkElement content, out double opacity)
    {
        opacity = 1;
        for (var parent = VisualTreeHelper.GetParent(content); parent is not null; parent = VisualTreeHelper.GetParent(parent))
        {
            if (parent is not FrameworkElement element) continue;
            opacity *= element.Opacity;
            if (element.Visibility != Visibility.Visible || element.ActualWidth <= 0 || element.ActualHeight <= 0 || opacity < 0.99)
            {
                return false;
            }
        }

        return true;
    }
#endif
}
