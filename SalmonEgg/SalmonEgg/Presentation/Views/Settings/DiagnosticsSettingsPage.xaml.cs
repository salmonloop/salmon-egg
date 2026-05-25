using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Extensions.Logging;
using SalmonEgg.Presentation.Models.Settings;
using SalmonEgg.Presentation.Core.Services.Input;
using SalmonEgg.Presentation.ViewModels.Settings;
using SalmonEgg.Presentation.Views;

namespace SalmonEgg.Presentation.Views.Settings;

public sealed partial class DiagnosticsSettingsPage : SettingsPageBase, INavigationIntentConsumer
{
    private ScrollViewer? _liveLogScrollViewer;
    private readonly ILogger<DiagnosticsSettingsPage> _logger;
    private Button? _lastFocusedGamepadActionButton;

    public DiagnosticsSettingsViewModel ViewModel { get; }

    public DiagnosticsSettingsPage()
    {
        _logger = App.ServiceProvider.GetRequiredService<ILogger<DiagnosticsSettingsPage>>();
        ViewModel = App.ServiceProvider.GetRequiredService<DiagnosticsSettingsViewModel>();
        InitializeComponent();
        SetSettingsBreadcrumbForSection(SettingsSectionCatalog.DiagnosticsKey);
        Unloaded += OnUnloaded;
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

    private void OnGamepadActionGotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            _lastFocusedGamepadActionButton = button;
        }
    }

    public bool TryConsumeNavigationIntent(GamepadNavigationIntent intent)
    {
        var consumed = intent switch
        {
            GamepadNavigationIntent.MoveDown => TryMoveFocusWithinGamepadActions(moveDown: true),
            GamepadNavigationIntent.MoveUp => TryMoveFocusWithinGamepadActions(moveDown: false),
            _ => false
        };

#if DEBUG
        App.BootLog($"DiagnosticsGamepad intent={intent} consumed={consumed}");
#endif

        return consumed;
    }

    private bool TryMoveFocusWithinGamepadActions(bool moveDown)
    {
        if (XamlRoot is null)
        {
            return false;
        }

        var focusedElement = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
        var focusedButton = ResolveFocusedGamepadActionButton(focusedElement);
        focusedButton ??= ResolveFocusedGamepadActionButtonFromControlState();
        focusedButton ??= _lastFocusedGamepadActionButton;
#if DEBUG
        App.BootLog($"DiagnosticsGamepad move={(moveDown ? "down" : "up")} focused={DescribeButton(focusedButton)}");
#endif
        if (focusedButton is null)
        {
            return false;
        }

        if (ReferenceEquals(focusedButton, DiagnosticsGamepadStartButton))
        {
            if (!moveDown)
            {
                return TryFocusOwnerSectionNavigation();
            }

            if (DiagnosticsGamepadStopButton.IsEnabled
                && DiagnosticsGamepadStopButton.Focus(FocusState.Programmatic))
            {
                _lastFocusedGamepadActionButton = DiagnosticsGamepadStopButton;
                return true;
            }

            if (DiagnosticsGamepadRefreshButton.IsEnabled
                && DiagnosticsGamepadRefreshButton.Focus(FocusState.Programmatic))
            {
                _lastFocusedGamepadActionButton = DiagnosticsGamepadRefreshButton;
                return true;
            }

            return false;
        }

        if (ReferenceEquals(focusedButton, DiagnosticsGamepadStopButton))
        {
            if (moveDown)
            {
                if (DiagnosticsGamepadRefreshButton.IsEnabled
                    && DiagnosticsGamepadRefreshButton.Focus(FocusState.Programmatic))
                {
                    _lastFocusedGamepadActionButton = DiagnosticsGamepadRefreshButton;
                    return true;
                }

                return false;
            }

            if (DiagnosticsGamepadStartButton.IsEnabled
                && DiagnosticsGamepadStartButton.Focus(FocusState.Programmatic))
            {
                _lastFocusedGamepadActionButton = DiagnosticsGamepadStartButton;
                return true;
            }

            return false;
        }

        if (ReferenceEquals(focusedButton, DiagnosticsGamepadRefreshButton))
        {
            if (moveDown)
            {
                return false;
            }

            if (DiagnosticsGamepadStopButton.IsEnabled
                && DiagnosticsGamepadStopButton.Focus(FocusState.Programmatic))
            {
                _lastFocusedGamepadActionButton = DiagnosticsGamepadStopButton;
                return true;
            }

            if (DiagnosticsGamepadStartButton.IsEnabled
                && DiagnosticsGamepadStartButton.Focus(FocusState.Programmatic))
            {
                _lastFocusedGamepadActionButton = DiagnosticsGamepadStartButton;
                return true;
            }

            return false;
        }

        return false;
    }

    private static string DescribeButton(Button? button)
    {
        if (button is null)
        {
            return "<null>";
        }

        var automationId = Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(button);
        var name = Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(button);
        var content = button.Content?.ToString();
        return $"{button.GetType().Name}(id={automationId ?? "<null>"},name={name ?? "<null>"},content={content ?? "<null>"})";
    }

    private static T? FindAncestorOrSelf<T>(DependencyObject? start)
        where T : DependencyObject
    {
        var current = start;
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private Button? ResolveFocusedGamepadActionButtonFromControlState()
    {
        if (DiagnosticsGamepadStartButton.FocusState != FocusState.Unfocused)
        {
            return DiagnosticsGamepadStartButton;
        }

        if (DiagnosticsGamepadStopButton.FocusState != FocusState.Unfocused)
        {
            return DiagnosticsGamepadStopButton;
        }

        if (DiagnosticsGamepadRefreshButton.FocusState != FocusState.Unfocused)
        {
            return DiagnosticsGamepadRefreshButton;
        }

        return null;
    }

    private bool TryFocusOwnerSectionNavigation()
    {
        return FindAncestorOrSelf<SettingsShellPage>(this)?.TryFocusSelectedSectionNavigationItemForChildPage() == true;
    }

    private Button? ResolveFocusedGamepadActionButton(DependencyObject? start)
    {
        var current = start;
        while (current is not null)
        {
            if (ReferenceEquals(current, DiagnosticsGamepadStartButton))
            {
                return DiagnosticsGamepadStartButton;
            }

            if (ReferenceEquals(current, DiagnosticsGamepadStopButton))
            {
                return DiagnosticsGamepadStopButton;
            }

            if (ReferenceEquals(current, DiagnosticsGamepadRefreshButton))
            {
                return DiagnosticsGamepadRefreshButton;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
