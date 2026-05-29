using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Collections.Generic;
using SalmonEgg.Presentation.Models.Settings;
using SalmonEgg.Presentation.ViewModels.Settings;
using SalmonEgg.Presentation.Views;

namespace SalmonEgg.Presentation.Views.Settings;

public sealed partial class McpSettingsPage : SettingsPageBase
{
    public McpSettingsViewModel ViewModel { get; }

    public McpSettingsPage()
    {
        ViewModel = App.ServiceProvider.GetRequiredService<McpSettingsViewModel>();
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        ViewModel.Servers.CollectionChanged += OnServersCollectionChanged;
        McpServersList.Loaded += OnServersListLoaded;
        SetSettingsBreadcrumbForSection(SettingsSectionCatalog.McpKey);
    }

    protected override Control? GetSectionEntryFocusTarget()
        => FirstAvailableSectionEntryTarget(McpReloadButton, McpAddServerButton);

    protected override IEnumerable<Control?> GetSectionFocusReturnTargets()
    {
        yield return McpReloadButton;
        yield return McpAddServerButton;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadCommand.ExecuteAsync(null);
        QueueRefreshTopActionFocusTargets();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        ViewModel.Servers.CollectionChanged -= OnServersCollectionChanged;
        McpServersList.Loaded -= OnServersListLoaded;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(McpSettingsViewModel.IsEditorOpen)
            or nameof(McpSettingsViewModel.EditingServer)
            or nameof(McpSettingsViewModel.IsLoading))
        {
            QueueRefreshTopActionFocusTargets();
        }
    }

    private void OnServersCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => QueueRefreshTopActionFocusTargets();

    private void OnServersListLoaded(object sender, RoutedEventArgs e)
        => QueueRefreshTopActionFocusTargets();

    private void QueueRefreshTopActionFocusTargets()
    {
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            _ = DispatcherQueue.TryEnqueue(RefreshTopActionFocusTargets);
        });
    }

    private void RefreshTopActionFocusTargets()
    {
        UpdateLayout();

        var downTarget = ResolveTopActionDownTarget();
        if (downTarget is null)
        {
            McpReloadButton.ClearValue(Control.XYFocusDownProperty);
            McpAddServerButton.ClearValue(Control.XYFocusDownProperty);
            return;
        }

        McpReloadButton.XYFocusDown = downTarget;
        McpAddServerButton.XYFocusDown = downTarget;
    }

    private Control? ResolveTopActionDownTarget()
        => FirstAvailableSectionEntryTarget(
            FindByAutomationId<Button>("Mcp.Editor.Close"),
            FindByAutomationId<Button>("Mcp.FillFromClipboardJson"),
            FindByAutomationId<ToggleSwitch>("Mcp.Server.Enabled"),
            FindByAutomationId<Button>("Mcp.EditServer"),
            FindByAutomationId<Button>("Mcp.RemoveServer"));

    private T? FindByAutomationId<T>(string automationId)
        where T : Control
        => FindDescendantControl<T>(control =>
            string.Equals(AutomationProperties.GetAutomationId(control), automationId, StringComparison.Ordinal));
}
