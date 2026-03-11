using System;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace SalmonEgg.Presentation.Views;

/// <summary>
/// Settings shell page.
/// - Breadcrumb (Settings / Section) at the top.
/// - Secondary navigation as a Top NavigationView below the breadcrumb.
/// - Section content hosted in an inner Frame.
/// </summary>
public sealed partial class SettingsShellPage : Page
{
    private bool _suppressSelection;

    public SettingsShellPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        var key = e.Parameter as string;
        NavigateToSection(string.IsNullOrWhiteSpace(key) ? "General" : key);
    }

    public void NavigateToSection(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || SettingsNavView is null || SettingsFrame is null)
        {
            return;
        }

        // Keep the secondary nav selection in sync (but avoid re-entrancy loops).
        var target = FindNavItemByKey(key) ?? FindNavItemByKey("General");
        if (target is null)
        {
            return;
        }

        _suppressSelection = true;
        try
        {
            SettingsNavView.SelectedItem = target;
        }
        finally
        {
            _suppressSelection = false;
        }

        var pageType = GetSettingsSectionPageType(key);
        if (SettingsFrame.CurrentSourcePageType != pageType)
        {
            SettingsFrame.Navigate(pageType);
        }
    }

    private void OnSettingsNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_suppressSelection)
        {
            return;
        }

        if (args.SelectedItem is NavigationViewItem item && item.Tag is string key)
        {
            NavigateToSection(key);
        }
    }

    private NavigationViewItem? FindNavItemByKey(string key)
    {
        foreach (var obj in SettingsNavView.MenuItems)
        {
            if (obj is NavigationViewItem item && item.Tag is string tag && string.Equals(tag, key, StringComparison.Ordinal))
            {
                return item;
            }
        }

        return null;
    }

    private static Type GetSettingsSectionPageType(string key) => key switch
    {
        "General" => typeof(GeneralSettingsPage),
        "Appearance" => typeof(Settings.AppearanceSettingsPage),
        "AgentAcp" => typeof(Settings.AcpConnectionSettingsPage),
        "DataStorage" => typeof(Settings.DataStorageSettingsPage),
        "Shortcuts" => typeof(Settings.ShortcutsSettingsPage),
        "Diagnostics" => typeof(Settings.DiagnosticsSettingsPage),
        "About" => typeof(Settings.AboutPage),
        _ => typeof(GeneralSettingsPage)
    };

}
