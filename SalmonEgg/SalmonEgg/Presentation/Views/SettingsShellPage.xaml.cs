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
        _ = DispatcherQueue.TryEnqueue(RefreshCurrentSectionFocusTargets);
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
#if DEBUG
        App.BootLog($"SettingsShell section invoked key={args.Key} focusBeforeNavigate={DescribeFocusedElement()}");
#endif
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

        _ = DispatcherQueue.TryEnqueue(RefreshCurrentSectionFocusTargets);
    }

    private void OnSettingsFrameNavigated(object sender, NavigationEventArgs e)
    {
#if DEBUG
        App.BootLog($"SettingsShell frame navigated page={e.SourcePageType?.Name ?? "<null>"} focusAfterNavigate={DescribeFocusedElement()}");
#endif
        _ = DispatcherQueue.TryEnqueue(RefreshCurrentSectionFocusTargets);
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
        if (intent == GamepadNavigationIntent.MoveDown && IsFocusWithinSettingsNav())
        {
            var consumed = TryFocusCurrentSectionContent();
#if DEBUG
            App.BootLog($"SettingsShellGamepad intent=MoveDown scope=nav consumed={consumed}");
#endif
            return consumed;
        }

        if (intent == GamepadNavigationIntent.MoveUp
            && IsFocusWithinSettingsContent()
            && IsFocusOnFirstSettingsContentControl())
        {
            var consumed = TryFocusCurrentSectionNavigationItem();
#if DEBUG
            App.BootLog($"SettingsShellGamepad intent=MoveUp scope=content-first consumed={consumed}");
#endif
            return consumed;
        }

        return false;
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
#if DEBUG
            App.BootLog("SettingsGamepad MoveDown nav->content target=" + DescribeControl(diagnosticsStart));
#endif
            return diagnosticsStart.Focus(FocusState.Programmatic);
        }

        var firstInteractive = GetInteractiveControlsInTraversalOrder().FirstOrDefault();
        if (firstInteractive is null)
        {
            return false;
        }

#if DEBUG
        App.BootLog("SettingsGamepad MoveDown nav->content target=" + DescribeControl(firstInteractive));
#endif
        return firstInteractive.Focus(FocusState.Programmatic);
    }

    private bool IsFocusWithinSettingsContent()
    {
        if (SettingsFrame.XamlRoot is null)
        {
            return false;
        }

        var current = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(SettingsFrame.XamlRoot) as DependencyObject;
        while (current is not null)
        {
            if (ReferenceEquals(current, SettingsFrame))
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private bool TryFocusCurrentSectionNavigationItem()
    {
        var automationId = ViewModel.SelectedSection.AutomationId;
        return FindDescendant<NavigationViewItem>(SettingsNavView, item =>
                string.Equals(Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(item), automationId, StringComparison.Ordinal))
            is { } selectedItem
            && selectedItem.Focus(FocusState.Programmatic);
    }

    internal bool TryFocusSelectedSectionNavigationItemForChildPage()
        => TryFocusCurrentSectionNavigationItem();

    private void RefreshCurrentSectionFocusTargets()
    {
        if (SettingsFrame.Content is null)
        {
            return;
        }

        var automationId = ViewModel.SelectedSection.AutomationId;
        var navItem = FindDescendant<NavigationViewItem>(SettingsNavView, item =>
            string.Equals(Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(item), automationId, StringComparison.Ordinal));
        if (navItem is null)
        {
            return;
        }

        var interactiveControls = GetInteractiveControlsInTraversalOrder().ToArray();
        var firstInteractive = interactiveControls.FirstOrDefault();
        if (firstInteractive is null)
        {
            return;
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
    }

    private IEnumerable<Control> GetInteractiveControlsInTraversalOrder()
    {
        if (SettingsFrame.Content is null)
        {
            return Enumerable.Empty<Control>();
        }

        return FindDescendants<Control>(SettingsFrame, static control =>
                control is ComboBox or ToggleSwitch or TextBox or Button)
            .Where(control => !HasInteractiveAncestor(control))
            .Where(IsUserMeaningfulInteractiveControl)
            .ToArray();
    }

    private bool IsFocusOnFirstSettingsContentControl()
    {
        if (SettingsFrame.XamlRoot is null)
        {
            return false;
        }

        var interactiveControls = GetInteractiveControlsInTraversalOrder().ToArray();
        if (interactiveControls.Length == 0)
        {
            return false;
        }

        var focusedElement = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(SettingsFrame.XamlRoot) as DependencyObject;
        var focusedControl = ResolveFocusedInteractiveControl(focusedElement, interactiveControls);
        return focusedControl is not null
               && ReferenceEquals(focusedControl, interactiveControls[0]);
    }

    private static bool HasInteractiveAncestor(DependencyObject control)
    {
        var current = VisualTreeHelper.GetParent(control);
        while (current is not null)
        {
            if (current is Control parentControl
                && parentControl is ComboBox or ToggleSwitch or TextBox or Button)
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
            ToggleSwitch => true,
            Button button => !string.IsNullOrWhiteSpace(Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(button))
                             || !string.IsNullOrWhiteSpace(button.Name)
                             || !string.IsNullOrWhiteSpace(button.Content?.ToString()),
            _ => false
        };
    }

    private static Control? ResolveFocusedInteractiveControl(DependencyObject? focusedElement, IReadOnlyList<Control> interactiveControls)
    {
        var current = focusedElement;
        while (current is not null)
        {
            if (current is Control control)
            {
                for (var i = 0; i < interactiveControls.Count; i++)
                {
                    if (ReferenceEquals(control, interactiveControls[i]))
                    {
                        return control;
                    }
                }
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static string DescribeControl(Control control)
    {
        var automationId = Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(control);
        var name = Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(control);
        var content = control switch
        {
            Button button => button.Content?.ToString(),
            _ => null
        };

        return $"{control.GetType().Name}(id={automationId ?? "<null>"},name={name ?? "<null>"},content={content ?? "<null>"})";
    }

    private string DescribeFocusedElement()
    {
        if (SettingsNavView.XamlRoot is null)
        {
            return "<xamlroot-null>";
        }

        var current = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(SettingsNavView.XamlRoot) as DependencyObject;
        if (current is null)
        {
            return "<focus-null>";
        }

        return DescribeDependencyObject(current);
    }

    private static string DescribeDependencyObject(DependencyObject current)
    {
        return current switch
        {
            Control control => DescribeControl(control),
            TextBlock textBlock => $"TextBlock(id={Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(textBlock) ?? "<null>"},name={Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(textBlock) ?? "<null>"},text={textBlock.Text ?? "<null>"})",
            _ => current.GetType().Name
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
