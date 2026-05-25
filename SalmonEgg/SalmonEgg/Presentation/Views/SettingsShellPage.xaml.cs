using System;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using SalmonEgg.Presentation.Core.Services.Input;
using SalmonEgg.Presentation.Models;
using SalmonEgg.Presentation.Models.Settings;
using SalmonEgg.Presentation.Navigation;
using SalmonEgg.Presentation.ViewModels.Settings;

namespace SalmonEgg.Presentation.Views;

/// <summary>
/// Settings shell page.
/// - Breadcrumb (Settings / Section) at the top.
/// - Secondary navigation as a Top NavigationView below the breadcrumb.
/// - Section content hosted in an inner Frame.
/// </summary>
public sealed partial class SettingsShellPage : Page, INavigationIntentConsumer
{
    private SettingsSectionNavigationAdapter? _sectionNavigation;

    public SettingsShellViewModel ViewModel { get; }

    public SettingsShellPage()
    {
        ViewModel = App.ServiceProvider.GetRequiredService<SettingsShellViewModel>();
        InitializeComponent();
        AttachSectionNavigation();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        var key = e.Parameter as string;
        NavigateToSection(string.IsNullOrWhiteSpace(key) ? SettingsSectionCatalog.GeneralKey : key);
    }

    public void NavigateToSection(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || SettingsFrame is null)
        {
            return;
        }

        var section = ViewModel.SelectSection(key);
        AttachSectionNavigation();
        NavigateFrameToSection(section.Key);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);

        DetachSectionNavigation();
    }

    private void AttachSectionNavigation()
    {
        if (_sectionNavigation is not null)
        {
            return;
        }

        _sectionNavigation = new SettingsSectionNavigationAdapter(SettingsNavView);
        _sectionNavigation.SectionInvoked += OnSectionNavigationInvoked;
    }

    private void DetachSectionNavigation()
    {
        if (_sectionNavigation is null)
        {
            return;
        }

        _sectionNavigation.SectionInvoked -= OnSectionNavigationInvoked;
        _sectionNavigation.Dispose();
        _sectionNavigation = null;
    }

    private void OnSectionNavigationInvoked(object? sender, SettingsSectionNavigationInvokedEventArgs args)
    {
        var section = ViewModel.SelectSection(args.Key);
        NavigateFrameToSection(section.Key);
    }

    private void NavigateFrameToSection(string key)
    {
        var pageType = GetSettingsSectionPageType(key);
        if (SettingsFrame.CurrentSourcePageType != pageType)
        {
            SettingsFrame.Navigate(pageType, null, UiMotionController.Current.CreateNavigationTransitionInfo());
        }
    }

    private static Type GetSettingsSectionPageType(string key) => key switch
    {
        SettingsSectionCatalog.GeneralKey => typeof(GeneralSettingsPage),
        SettingsSectionCatalog.AppearanceKey => typeof(Settings.AppearanceSettingsPage),
        SettingsSectionCatalog.AgentAcpKey => typeof(Settings.AcpConnectionSettingsPage),
        SettingsSectionCatalog.McpKey => typeof(Settings.McpSettingsPage),
        SettingsSectionCatalog.DataStorageKey => typeof(Settings.DataStorageSettingsPage),
        SettingsSectionCatalog.ShortcutsKey => typeof(Settings.ShortcutsSettingsPage),
        SettingsSectionCatalog.DiagnosticsKey => typeof(Settings.DiagnosticsSettingsPage),
        SettingsSectionCatalog.AboutKey => typeof(Settings.AboutPage),
        _ => typeof(GeneralSettingsPage)
    };

    public bool TryConsumeNavigationIntent(GamepadNavigationIntent intent)
    {
        if (intent != GamepadNavigationIntent.MoveDown)
        {
            return false;
        }

        if (!IsFocusWithinSettingsNav())
        {
            return false;
        }

        return TryFocusCurrentSectionContent();
    }

    private bool IsFocusWithinSettingsNav()
    {
        if (SettingsNavView.XamlRoot is null)
        {
            return false;
        }

        var current = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(SettingsNavView.XamlRoot) as DependencyObject;
        while (current is not null)
        {
            if (ReferenceEquals(current, SettingsNavView))
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private bool TryFocusCurrentSectionContent()
    {
        if (SettingsFrame.Content is null)
        {
            return false;
        }

        if (FindDescendant<Button>(SettingsFrame, static button =>
                string.Equals(Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(button), "Diagnostics.GamepadStart", StringComparison.Ordinal)) is { } diagnosticsStart)
        {
            return diagnosticsStart.Focus(FocusState.Programmatic);
        }

        return FindDescendant<Control>(SettingsFrame, static control =>
                control is ComboBox or ToggleSwitch or TextBox or Button)
            is { } firstInteractive
            && firstInteractive.Focus(FocusState.Programmatic);
    }

    private static T? FindDescendant<T>(DependencyObject root, Func<T, bool> predicate)
        where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match && predicate(match))
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
