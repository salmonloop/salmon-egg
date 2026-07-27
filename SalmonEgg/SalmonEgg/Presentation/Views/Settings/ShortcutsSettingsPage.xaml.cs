using System.ComponentModel;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SalmonEgg.Controls;
using SalmonEgg.Presentation.Models.Settings;
using SalmonEgg.Presentation.ViewModels.Settings;
using SalmonEgg.Presentation.Views;

namespace SalmonEgg.Presentation.Views.Settings;

public sealed partial class ShortcutsSettingsPage : SettingsPageBase
{
    private const string RecorderAutomationIdPrefix = "Shortcuts.Record.";
    private const string RestoreAutomationIdPrefix = "Shortcuts.Restore.";

    public ShortcutsSettingsViewModel ViewModel { get; }

    public ShortcutsSettingsPage()
    {
        ViewModel = App.ServiceProvider.GetRequiredService<ShortcutsSettingsViewModel>();
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SetSettingsBreadcrumbForSection(SettingsSectionCatalog.ShortcutsKey);
    }

    protected override Control? GetSectionEntryFocusTarget()
    {
        return FirstAvailableSectionEntryTarget(
            ShortcutsEnabledToggle,
            FindDescendantControl<Button>(button => string.Equals(
                Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(button),
                "Shortcuts.RestoreAll",
                StringComparison.Ordinal)));
    }

    protected override IEnumerable<Control?> GetSectionFocusReturnTargets()
    {
        yield return ShortcutsEnabledToggle;

        var (recorderButton, restoreButton, _, _) = ResolveShortcutInteractionTargets();
        if (recorderButton?.IsEnabled == true || recorderButton?.AllowFocusWhenDisabled == true)
        {
            yield return recorderButton;
        }

        if (restoreButton?.IsEnabled == true || restoreButton?.AllowFocusWhenDisabled == true)
        {
            yield return restoreButton;
        }
    }

    private (Button? FirstRecorderButton, Button? FirstRestoreButton, Button? LastRecorderButton, Button? LastRestoreButton) ResolveShortcutInteractionTargets()
    {
        ShortcutsItemsHost.UpdateLayout();
        var recorderButtons = FindDescendantControls(ShortcutsItemsHost, IsShortcutRecorderButton);
        var restoreButtons = FindDescendantControls(ShortcutsItemsHost, IsShortcutRestoreButton);
        var firstRecorderButton = recorderButtons.FirstOrDefault();
        var firstRestoreButton = restoreButtons.FirstOrDefault();
        var lastRecorderButton = recorderButtons.LastOrDefault();
        var lastRestoreButton = restoreButtons.LastOrDefault();

        return (
            firstRecorderButton,
            firstRestoreButton,
            lastRecorderButton,
            lastRestoreButton);
    }

    private void OnLoaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        ViewModel.Activate();
        ViewModel.Preferences.PropertyChanged -= OnPreferencesPropertyChanged;
        ViewModel.Preferences.PropertyChanged += OnPreferencesPropertyChanged;
        QueueRefreshShortcutDirectionalTargets();
    }

    private void OnUnloaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        ViewModel.Preferences.PropertyChanged -= OnPreferencesPropertyChanged;
        ViewModel.Deactivate();
    }

    private void OnPreferencesPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(AppPreferencesViewModel.KeyboardShortcutsEnabled), StringComparison.Ordinal))
        {
            QueueRefreshShortcutDirectionalTargets();
        }
    }

    private void QueueRefreshShortcutDirectionalTargets()
    {
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            _ = DispatcherQueue.TryEnqueue(RefreshShortcutDirectionalTargets);
        });
    }

    private void RefreshShortcutDirectionalTargets()
    {
        UpdateLayout();

        var (firstRecorderButton, firstRestoreButton, lastRecorderButton, lastRestoreButton) = ResolveShortcutInteractionTargets();
        ShortcutsEnabledToggle.ClearValue(Control.XYFocusDownProperty);
        RestoreAllButton.ClearValue(Control.XYFocusUpProperty);
        lastRecorderButton?.ClearValue(Control.XYFocusDownProperty);
        lastRestoreButton?.ClearValue(Control.XYFocusDownProperty);

        var downTarget = FirstAvailableSectionEntryTarget(firstRecorderButton, firstRestoreButton);
        if (downTarget is not null)
        {
            ShortcutsEnabledToggle.XYFocusDown = downTarget;
        }

        var trailingTarget = FirstAvailableSectionEntryTarget(lastRestoreButton, lastRecorderButton);
        if (trailingTarget is not null)
        {
            RestoreAllButton.XYFocusUp = trailingTarget;
            trailingTarget.XYFocusDown = RestoreAllButton;
        }

        FindAncestor<SettingsShellPage>(this)?.RefreshCurrentSectionFocusTargetsForChildPage();
    }

    private static bool IsShortcutRecorderButton(Button button)
        => Microsoft.UI.Xaml.Automation.AutomationProperties
            .GetAutomationId(button)
            .StartsWith(RecorderAutomationIdPrefix, StringComparison.Ordinal);

    private static bool IsShortcutRestoreButton(Button button)
        => Microsoft.UI.Xaml.Automation.AutomationProperties
            .GetAutomationId(button)
            .StartsWith(RestoreAutomationIdPrefix, StringComparison.Ordinal);

    private static List<Button> FindDescendantControls(DependencyObject root, Func<Button, bool> predicate)
    {
        var matches = new List<Button>();
        CollectDescendantControls(root, predicate, matches);
        return matches;
    }

    private static void CollectDescendantControls(
        DependencyObject root,
        Func<Button, bool> predicate,
        ICollection<Button> matches)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is Button button && predicate(button))
            {
                matches.Add(button);
            }

            CollectDescendantControls(child, predicate, matches);
        }
    }

    private static T? FindAncestor<T>(DependencyObject start)
        where T : DependencyObject
    {
        var current = VisualTreeHelper.GetParent(start);
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return default;
    }
}
