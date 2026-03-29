using System.Collections.Generic;
using System.Collections.ObjectModel;
using Microsoft.UI.Xaml.Controls;
using SalmonEgg.Presentation.Models.Navigation;
using Windows.ApplicationModel.Resources;

namespace SalmonEgg.Presentation.Views;

/// <summary>
/// Base class for Settings sub-pages providing a standard BreadcrumbBar model.
/// </summary>
public class SettingsPageBase : Page
{
    private static readonly ResourceLoader ResourceLoader = ResourceLoader.GetForViewIndependentUse();

    public ObservableCollection<SettingsBreadcrumbItem> BreadcrumbItems { get; } = new();

    protected void SetSettingsBreadcrumb(string currentText)
    {
        SetBreadcrumb(
            SettingsBreadcrumbItem.Link(ResolveResourceString("SettingsBreadcrumbRoot", "设置"), "General"),
            SettingsBreadcrumbItem.Current(currentText));
    }

    protected void SetSettingsBreadcrumbFromResource(string currentResourceKey, string fallbackText)
    {
        SetSettingsBreadcrumb(ResolveResourceString(currentResourceKey, fallbackText));
    }

    protected void SetBreadcrumb(params SettingsBreadcrumbItem[] items)
    {
        BreadcrumbItems.Clear();
        foreach (var item in items)
        {
            BreadcrumbItems.Add(item);
        }
    }

    protected static string ResolveResourceString(string resourceKey, string fallbackText)
    {
        var value = ResourceLoader.GetString(resourceKey);
        return string.IsNullOrWhiteSpace(value) ? fallbackText : value;
    }
}
