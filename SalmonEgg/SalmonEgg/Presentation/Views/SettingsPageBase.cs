using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.Extensions.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SalmonEgg.Presentation.Core.Resources;
using SalmonEgg.Presentation.Models.Navigation;
using SalmonEgg.Presentation.Models.Settings;
using Windows.ApplicationModel.Resources;

namespace SalmonEgg.Presentation.Views;

/// <summary>
/// Base class for Settings sub-pages providing a standard BreadcrumbBar model.
/// </summary>
public class SettingsPageBase : Page
{
    private static readonly ResourceLoader ResourceLoader = ResourceLoader.GetForViewIndependentUse();

    private readonly IStringLocalizer<CoreStrings> _coreStrings;

    protected SettingsPageBase()
    {
        _coreStrings = App.ServiceProvider.GetRequiredService<IStringLocalizer<CoreStrings>>();
    }

    public ObservableCollection<SettingsBreadcrumbItem> BreadcrumbItems { get; } = new();

    protected void SetSettingsBreadcrumb(string currentText)
    {
        SetBreadcrumb(
            SettingsBreadcrumbItem.Link(ResolveSettingsRootTitle(), SettingsSectionCatalog.GeneralKey),
            SettingsBreadcrumbItem.Current(currentText));
    }

    protected void SetSettingsBreadcrumbForSection(string sectionKey)
    {
        SetSettingsBreadcrumb(ResolveSettingsSectionTitle(sectionKey));
    }

    protected void SetBreadcrumb(params SettingsBreadcrumbItem[] items)
    {
        BreadcrumbItems.Clear();
        foreach (var item in items)
        {
            BreadcrumbItems.Add(item);
        }
    }

    protected string ResolveSettingsRootTitle()
        => SettingsSectionCatalog.ResolveRootTitle(_coreStrings);

    protected string ResolveSettingsSectionTitle(string sectionKey)
        => SettingsSectionCatalog.ResolveTitle(_coreStrings, sectionKey);

    protected static string ResolveResourceString(string resourceKey, string fallbackText)
    {
        var value = ResourceLoader.GetString(resourceKey);
        return string.IsNullOrWhiteSpace(value) ? fallbackText : value;
    }

    protected virtual Control? GetSectionEntryFocusTarget() => null;

    internal Control? TryGetSectionEntryFocusTarget()
        => GetSectionEntryFocusTarget();

    protected virtual IEnumerable<Control?> GetSectionFocusReturnTargets()
    {
        yield return GetSectionEntryFocusTarget();
    }

    internal IReadOnlyList<Control> TryGetSectionFocusReturnTargets()
        => GetSectionFocusReturnTargets()
            .Where(CanHoldSectionReturnFocusTarget)
            .Distinct()
            .Cast<Control>()
            .ToArray();

    protected Control? FirstAvailableSectionEntryTarget(params Control?[] candidates)
        => candidates.FirstOrDefault(CanReceiveSectionEntryFocus);

    protected T? FindDescendantControl<T>(Func<T, bool>? predicate = null)
        where T : Control
        => FindDescendant(this, predicate);

    private static bool CanReceiveSectionEntryFocus(Control? candidate)
    {
        if (candidate is null)
        {
            return false;
        }

        if (!IsEffectivelyVisible(candidate))
        {
            return false;
        }

        return candidate.IsEnabled || candidate.AllowFocusWhenDisabled;
    }

    private static bool CanHoldSectionReturnFocusTarget(Control? candidate)
    {
        if (candidate is null)
        {
            return false;
        }

        return IsEffectivelyVisible(candidate);
    }

    private static bool IsEffectivelyVisible(Control candidate)
    {
        DependencyObject? current = candidate;
        while (current is not null)
        {
            if (current is UIElement element && element.Visibility != Visibility.Visible)
            {
                return false;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return true;
    }

    private static T? FindDescendant<T>(DependencyObject root, Func<T, bool>? predicate)
        where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match && (predicate is null || predicate(match)))
            {
                return match;
            }

            var nested = FindDescendant(child, predicate);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }
}
