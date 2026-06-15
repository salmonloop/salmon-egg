using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SalmonEgg.Presentation.Models.Settings;
using SalmonEgg.Presentation.ViewModels.Settings;
using SalmonEgg.Presentation.Views;

namespace SalmonEgg.Presentation.Views.Settings;

public sealed partial class DiagnosticsSettingsPage : SettingsPageBase
{
    private ScrollViewer? _liveLogScrollViewer;
    private readonly ILogger<DiagnosticsSettingsPage> _logger;

    public DiagnosticsSettingsViewModel ViewModel { get; }

    public DiagnosticsSettingsPage()
    {
        _logger = App.ServiceProvider.GetRequiredService<ILogger<DiagnosticsSettingsPage>>();
        ViewModel = App.ServiceProvider.GetRequiredService<DiagnosticsSettingsViewModel>();
        InitializeComponent();
        SetSettingsBreadcrumbForSection(SettingsSectionCatalog.DiagnosticsKey);
        Unloaded += OnUnloaded;
    }

    protected override Control? GetSectionEntryFocusTarget()
        => FirstAvailableSectionEntryTarget(
            DiagnosticsGamepadStartButton,
            DiagnosticsGamepadStopButton,
            DiagnosticsGamepadRefreshButton);

    protected override IEnumerable<Control?> GetSectionFocusReturnTargets()
    {
        yield return DiagnosticsGamepadStartButton;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _liveLogScrollViewer = null;
        _ = HandlePageUnloadedAsync();
    }

    private void OnLiveLogTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!ViewModel.LiveLogViewer.IsStreaming || !ViewModel.LiveLogViewer.IsAutoFollowEnabled)
        {
            return;
        }

        if (!DispatcherQueue.TryEnqueue(ScrollLiveLogToBottom))
        {
            ScrollLiveLogToBottom();
        }
    }

    private void ScrollLiveLogToBottom()
    {
        LiveLogTextBox.UpdateLayout();
        _liveLogScrollViewer ??= FindScrollViewer(LiveLogTextBox);
        _liveLogScrollViewer?.ChangeView(null, _liveLogScrollViewer.ScrollableHeight, null);
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject element)
    {
        if (element is ScrollViewer scrollViewer)
        {
            return scrollViewer;
        }

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
        {
            var child = VisualTreeHelper.GetChild(element, index);
            var result = FindScrollViewer(child);
            if (result is not null)
            {
                return result;
            }
        }

        return null;
    }

    private async Task HandlePageUnloadedAsync()
    {
        try
        {
            await ViewModel.LiveLogViewer.HandlePageUnloadedAsync();
            await ViewModel.GamepadDiagnostics.HandlePageUnloadedAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Diagnostics page unload cleanup failed");
        }
    }

}
