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
public sealed partial class SettingsShellPage : Page, IPrimaryContentFocusTarget, IGamepadContextIntentConsumer
{
    private SettingsSectionNavigationAdapter? _sectionNavigation;
    private SettingsPageBase? _pendingFocusTargetRefreshPage;

    public SettingsShellViewModel ViewModel { get; }

    public SettingsShellPage()
    {
        ViewModel = App.ServiceProvider.GetRequiredService<SettingsShellViewModel>();
        InitializeComponent();
        AttachSectionNavigation();
        SettingsFrame.Navigated += OnSettingsFrameNavigated;
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
        QueueRefreshCurrentSectionFocusTargets();
        QueueFocusCurrentSectionNavigationItem();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);

        SettingsFrame.Navigated -= OnSettingsFrameNavigated;
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
        QueueFocusCurrentSectionNavigationItem();
    }

    private void QueueFocusCurrentSectionNavigationItem()
    {
        _ = DispatcherQueue.TryEnqueue(() => _ = TryFocusCurrentSectionNavigationItem());
    }

    private void NavigateFrameToSection(string key)
    {
        var pageType = GetSettingsSectionPageType(key);
        if (SettingsFrame.CurrentSourcePageType != pageType)
        {
            SettingsFrame.Navigate(pageType, null, UiMotionController.Current.CreateNavigationTransitionInfo());
        }

        QueueRefreshCurrentSectionFocusTargets();
    }

    private void OnSettingsFrameNavigated(object sender, NavigationEventArgs e)
    {
        QueueRefreshCurrentSectionFocusTargets();
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

    private bool TryFocusCurrentSectionNavigationItem()
    {
        SettingsNavView.UpdateLayout();
        if (SettingsNavView.ContainerFromMenuItem(ViewModel.SelectedSection) is Control selectedContainer)
        {
            if (selectedContainer.Focus(FocusState.Keyboard))
            {
                return true;
            }
        }

        return false;
    }

    internal bool TryFocusSelectedSectionNavigationItemForChildPage()
        => TryFocusCurrentSectionNavigationItem();

    internal void RefreshCurrentSectionFocusTargetsForChildPage()
    {
        _ = DispatcherQueue.TryEnqueue(() => _ = TryRefreshCurrentSectionFocusTargets());
    }

    public bool TryFocusPrimaryContentTarget()
        => TryFocusCurrentSectionNavigationItem();

    public bool TryConsumeContextIntent(GamepadContextIntent intent)
    {
        if (SettingsFrame.Content is not SettingsPageBase settingsPage)
        {
            return false;
        }

        return settingsPage.TryConsumeContextIntent(intent, requireFocusedDescendant: false);
    }

    private void QueueRefreshCurrentSectionFocusTargets()
    {
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (!TryRefreshCurrentSectionFocusTargets())
            {
                DeferCurrentSectionFocusTargetRefresh();
            }
        });
    }

    private bool TryRefreshCurrentSectionFocusTargets()
    {
        if (SettingsFrame.Content is not SettingsPageBase settingsPage)
        {
            return false;
        }

        var automationId = ViewModel.SelectedSection.AutomationId;
        var navItem = FindDescendant<NavigationViewItem>(SettingsNavView, item =>
            string.Equals(Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(item), automationId, StringComparison.Ordinal));
        if (navItem is null)
        {
            return false;
        }

        var sectionEntryTarget = settingsPage.TryGetSectionEntryFocusTarget();
        if (sectionEntryTarget is null)
        {
            return false;
        }

        navItem.XYFocusDown = sectionEntryTarget;
        var returnTargets = settingsPage.TryGetSectionFocusReturnTargets();
        if (returnTargets.Count == 0)
        {
            returnTargets = [sectionEntryTarget];
        }

        foreach (var returnTarget in returnTargets)
        {
            returnTarget.XYFocusUp = navItem;
        }

        DetachDeferredFocusTargetRefresh(settingsPage);

        return true;
    }

    private void DeferCurrentSectionFocusTargetRefresh()
    {
        if (SettingsFrame.Content is not SettingsPageBase settingsPage)
        {
            return;
        }

        if (ReferenceEquals(_pendingFocusTargetRefreshPage, settingsPage))
        {
            return;
        }

        if (_pendingFocusTargetRefreshPage is not null)
        {
            DetachDeferredFocusTargetRefresh(_pendingFocusTargetRefreshPage);
        }

        _pendingFocusTargetRefreshPage = settingsPage;
        settingsPage.Loaded += OnDeferredFocusTargetRefreshLoaded;
    }

    private void DetachDeferredFocusTargetRefresh(SettingsPageBase settingsPage)
    {
        settingsPage.Loaded -= OnDeferredFocusTargetRefreshLoaded;
        if (ReferenceEquals(_pendingFocusTargetRefreshPage, settingsPage))
        {
            _pendingFocusTargetRefreshPage = null;
        }
    }

    private void OnDeferredFocusTargetRefreshLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not SettingsPageBase settingsPage)
        {
            return;
        }

        DetachDeferredFocusTargetRefresh(settingsPage);
        _ = TryRefreshCurrentSectionFocusTargets();
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

        return default;
    }

}
