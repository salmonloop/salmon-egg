using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SalmonEgg.Presentation.Models.Settings;
using SalmonEgg.Presentation.Core.Services.Input;
using SalmonEgg.Presentation.ViewModels.Settings;

namespace SalmonEgg.Presentation.Views.Settings;

public sealed partial class AppearanceSettingsPage : SalmonEgg.Presentation.Views.SettingsPageBase, INavigationIntentConsumer
{
    public AppPreferencesViewModel Preferences { get; }

    public AppearanceSettingsPage()
    {
        Preferences = App.ServiceProvider.GetRequiredService<AppPreferencesViewModel>();
        InitializeComponent();
        SetSettingsBreadcrumbForSection(SettingsSectionCatalog.AppearanceKey);
    }

    public bool TryConsumeNavigationIntent(GamepadNavigationIntent intent)
    {
        return intent switch
        {
            GamepadNavigationIntent.MoveDown => TryMoveFocusWithinAppearanceControls(moveDown: true),
            GamepadNavigationIntent.MoveUp => TryMoveFocusWithinAppearanceControls(moveDown: false),
            _ => false
        };
    }

    private bool TryMoveFocusWithinAppearanceControls(bool moveDown)
    {
        if (XamlRoot is null)
        {
            return false;
        }

        var focusedElement = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
        var current = ResolveFocusedAppearanceControl(focusedElement);
        if (current is null)
        {
            return false;
        }

        if (ReferenceEquals(current, AppearanceThemeComboBox))
        {
            return moveDown
                ? AppearanceAnimationToggle.Focus(FocusState.Programmatic)
                : false;
        }

        if (ReferenceEquals(current, AppearanceAnimationToggle))
        {
            return moveDown
                ? AppearanceBackdropComboBox.Focus(FocusState.Programmatic)
                : AppearanceThemeComboBox.Focus(FocusState.Programmatic);
        }

        if (ReferenceEquals(current, AppearanceBackdropComboBox))
        {
            return moveDown
                ? false
                : AppearanceAnimationToggle.Focus(FocusState.Programmatic);
        }

        return false;
    }

    private DependencyObject? ResolveFocusedAppearanceControl(DependencyObject? start)
    {
        var current = start;
        while (current is not null)
        {
            if (ReferenceEquals(current, AppearanceThemeComboBox)
                || ReferenceEquals(current, AppearanceAnimationToggle)
                || ReferenceEquals(current, AppearanceBackdropComboBox))
            {
                return current;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private void OnThemeComboBoxPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (sender is not ComboBox comboBox || comboBox.IsDropDownOpen)
        {
            return;
        }

        if (e.Key is Windows.System.VirtualKey.Down or Windows.System.VirtualKey.GamepadDPadDown)
        {
            if (AppearanceAnimationToggle.Focus(FocusState.Programmatic))
            {
                e.Handled = true;
            }
        }
    }

    private void OnAnimationToggleKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is Windows.System.VirtualKey.Up or Windows.System.VirtualKey.GamepadDPadUp)
        {
            if (AppearanceThemeComboBox.Focus(FocusState.Programmatic))
            {
                e.Handled = true;
            }
        }
        else if (e.Key is Windows.System.VirtualKey.Down or Windows.System.VirtualKey.GamepadDPadDown)
        {
            if (AppearanceBackdropComboBox.Focus(FocusState.Programmatic))
            {
                e.Handled = true;
            }
        }
    }

    private void OnBackdropComboBoxPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (sender is not ComboBox comboBox || comboBox.IsDropDownOpen)
        {
            return;
        }

        if (e.Key is Windows.System.VirtualKey.Up or Windows.System.VirtualKey.GamepadDPadUp)
        {
            if (AppearanceAnimationToggle.Focus(FocusState.Programmatic))
            {
                e.Handled = true;
            }
        }
    }
}
