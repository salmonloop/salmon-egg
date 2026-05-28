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
public sealed partial class SettingsShellPage : Page, IPrimaryContentFocusTarget
{
    private SettingsSectionNavigationAdapter? _sectionNavigation;
    private FrameworkElement? _pendingFocusTargetRefreshRoot;

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
        _ = DispatcherQueue.TryEnqueue(RefreshOrDeferCurrentSectionFocusTargets);
        QueueFocusCurrentSectionNavigationItem();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);

        SettingsFrame.Navigated -= OnSettingsFrameNavigated;
        DetachDeferredFocusTargetRefresh();
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

        _ = DispatcherQueue.TryEnqueue(RefreshOrDeferCurrentSectionFocusTargets);
    }

    private void OnSettingsFrameNavigated(object sender, NavigationEventArgs e)
    {
        _ = DispatcherQueue.TryEnqueue(RefreshOrDeferCurrentSectionFocusTargets);
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

    public bool TryFocusPrimaryContentTarget()
        => TryFocusCurrentSectionNavigationItem();

    private void RefreshOrDeferCurrentSectionFocusTargets()
    {
        if (TryRefreshCurrentSectionFocusTargets())
        {
            DetachDeferredFocusTargetRefresh();
            return;
        }

        if (SettingsFrame.Content is FrameworkElement root)
        {
            AttachDeferredFocusTargetRefresh(root);
        }
    }

    private bool TryRefreshCurrentSectionFocusTargets()
    {
        if (SettingsFrame.Content is null)
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

        var interactiveControls = GetInteractiveControlsInTraversalOrder().ToArray();
        var firstInteractive = interactiveControls.FirstOrDefault();
        if (firstInteractive is null)
        {
            return false;
        }

        navItem.XYFocusDown = firstInteractive;
        firstInteractive.XYFocusUp = navItem;

        for (var i = 0; i < interactiveControls.Length; i++)
        {
            var current = interactiveControls[i];
            current.XYFocusUp = i == 0 ? navItem : interactiveControls[i - 1];
            current.XYFocusDown = i + 1 < interactiveControls.Length
                ? interactiveControls[i + 1]
                : null;
        }

        return true;
    }

    private void AttachDeferredFocusTargetRefresh(FrameworkElement root)
    {
        if (ReferenceEquals(_pendingFocusTargetRefreshRoot, root))
        {
            return;
        }

        DetachDeferredFocusTargetRefresh();
        _pendingFocusTargetRefreshRoot = root;
        root.Loaded += OnDeferredFocusTargetRefreshLoaded;
        root.LayoutUpdated += OnDeferredFocusTargetRefreshLayoutUpdated;
    }

    private void DetachDeferredFocusTargetRefresh()
    {
        if (_pendingFocusTargetRefreshRoot is null)
        {
            return;
        }

        _pendingFocusTargetRefreshRoot.Loaded -= OnDeferredFocusTargetRefreshLoaded;
        _pendingFocusTargetRefreshRoot.LayoutUpdated -= OnDeferredFocusTargetRefreshLayoutUpdated;
        _pendingFocusTargetRefreshRoot = null;
    }

    private void OnDeferredFocusTargetRefreshLoaded(object sender, RoutedEventArgs e)
    {
        _ = DispatcherQueue.TryEnqueue(RefreshOrDeferCurrentSectionFocusTargets);
    }

    private void OnDeferredFocusTargetRefreshLayoutUpdated(object sender, object e)
    {
        if (!ReferenceEquals(sender, _pendingFocusTargetRefreshRoot))
        {
            return;
        }

        RefreshOrDeferCurrentSectionFocusTargets();
    }

    private IEnumerable<Control> GetInteractiveControlsInTraversalOrder()
    {
        if (SettingsFrame.Content is null)
        {
            return Enumerable.Empty<Control>();
        }

        return FindDescendants<Control>(SettingsFrame, static control =>
                control is ComboBox or NumberBox or ToggleSwitch or TextBox or Button or Expander)
            .Where(control => !HasInteractiveAncestor(control))
            .Where(IsUserMeaningfulInteractiveControl)
            .ToArray();
    }

    private static bool HasInteractiveAncestor(DependencyObject control)
    {
        var current = VisualTreeHelper.GetParent(control);
        while (current is not null)
        {
            if (current is Control parentControl
                && parentControl is ComboBox or NumberBox or ToggleSwitch or TextBox or Button)
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private static bool IsUserMeaningfulInteractiveControl(Control control)
    {
        if (control.Visibility != Visibility.Visible
            || !control.IsEnabled
            || control.ActualWidth <= 0
            || control.ActualHeight <= 0)
        {
            return false;
        }

        return control switch
        {
            TextBox => true,
            ComboBox => true,
            NumberBox => true,
            ToggleSwitch => true,
            Expander => true,
            Button button => !string.IsNullOrWhiteSpace(Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(button))
                             || !string.IsNullOrWhiteSpace(button.Name)
                             || !string.IsNullOrWhiteSpace(button.Content?.ToString()),
            _ => false
        };
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

    private static System.Collections.Generic.IEnumerable<T> FindDescendants<T>(DependencyObject root, Func<T, bool> predicate)
        where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match && predicate(match))
            {
                yield return match;
            }

            foreach (var nested in FindDescendants(child, predicate))
            {
                yield return nested;
            }
        }
    }

    private static T? FindAncestorOrSelf<T>(DependencyObject? start, Func<T, bool>? predicate = null)
        where T : DependencyObject
    {
        var current = start;
        while (current is not null)
        {
            if (current is T match && (predicate is null || predicate(match)))
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

}
