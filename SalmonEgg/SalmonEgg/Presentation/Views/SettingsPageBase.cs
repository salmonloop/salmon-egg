using System.Collections.Generic;
using System.Collections.ObjectModel;
using Microsoft.UI.Xaml.Controls;
using SalmonEgg.Presentation.Models.Navigation;

namespace SalmonEgg.Presentation.Views;

/// <summary>
/// Base class for Settings sub-pages providing a standard BreadcrumbBar model.
/// </summary>
public class SettingsPageBase : Page
{
    public ObservableCollection<SettingsBreadcrumbItem> BreadcrumbItems { get; } = new();

    protected void SetSettingsBreadcrumb(string currentText)
    {
        SetBreadcrumb(
            SettingsBreadcrumbItem.Link("设置", "General"),
            SettingsBreadcrumbItem.Current(currentText));
    }

    protected void SetBreadcrumb(params SettingsBreadcrumbItem[] items)
    {
        BreadcrumbItems.Clear();
        foreach (var item in items)
        {
            BreadcrumbItems.Add(item);
        }
    }
}
