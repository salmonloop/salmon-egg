using System.Collections.Generic;
using System.Collections.ObjectModel;
using Microsoft.Extensions.Localization;
using Microsoft.UI.Xaml.Controls;
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
}
