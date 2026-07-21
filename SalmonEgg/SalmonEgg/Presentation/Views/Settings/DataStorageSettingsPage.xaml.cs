using Microsoft.UI.Xaml.Controls;
using SalmonEgg.Presentation.Models.Settings;
using SalmonEgg.Presentation.ViewModels.Settings;
using SalmonEgg.Presentation.Utilities;
using SalmonEgg.Presentation.Views;
using Windows.ApplicationModel.Resources;

namespace SalmonEgg.Presentation.Views.Settings;

public sealed partial class DataStorageSettingsPage : SettingsPageBase
{
    private static readonly ResourceLoader ResourceLoader = ResourceLoader.GetForViewIndependentUse();

    public DataStorageSettingsViewModel ViewModel { get; }

    public DataStorageSettingsPage()
    {
        ViewModel = App.ServiceProvider.GetRequiredService<DataStorageSettingsViewModel>();
        InitializeComponent();
        SetSettingsBreadcrumbForSection(SettingsSectionCatalog.DataStorageKey);
    }

    protected override Control? GetSectionEntryFocusTarget()
        => DataStorageSaveLocalHistoryToggle;

    private void OnWebDavPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox passwordBox)
        {
            ViewModel.CloudConfig.WebDavPassword = passwordBox.Password;
        }
    }

    private void OnS3SecretAccessKeyChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox passwordBox)
        {
            ViewModel.CloudConfig.S3SecretAccessKey = passwordBox.Password;
        }
    }

    private async void OnClearCacheClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = ResolveString("DataStorage_ClearCacheDialog.Title", "Clear cache"),
            Content = ResolveString("DataStorage_ClearCacheDialog.Content", "This deletes all files in the local cache folder."),
            PrimaryButtonText = ResolveString("DataStorage_ClearCacheDialog.PrimaryButtonText", "Clear"),
            SecondaryButtonText = ResolveString("DataStorage_ClearCacheDialog.SecondaryButtonText", "Cancel"),
            DefaultButton = ContentDialogButton.Primary
        };
        ContentDialogHost.AttachToElement(dialog, this);

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            await ViewModel.ClearCacheCommand.ExecuteAsync(null);
        }
    }

    private async void OnResetPreferencesClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = ResolveString("DataStorage_ResetPreferencesDialog.Title", "Restore defaults"),
            Content = ResolveString("DataStorage_ResetPreferencesDialog.Content", "This restores General, Appearance, Data & Storage, Shortcuts, and related settings to their defaults."),
            PrimaryButtonText = ResolveString("DataStorage_ResetPreferencesDialog.PrimaryButtonText", "Restore"),
            SecondaryButtonText = ResolveString("DataStorage_ResetPreferencesDialog.SecondaryButtonText", "Cancel"),
            DefaultButton = ContentDialogButton.Primary
        };
        ContentDialogHost.AttachToElement(dialog, this);

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            ViewModel.Preferences.ResetToDefaults();
        }
    }

    private async void OnClearAllLocalDataClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = ResolveString("DataStorage_ClearAllLocalDataDialog.Title", "Clear all local data"),
            Content = ResolveString("DataStorage_ClearAllLocalDataDialog.Content", "This deletes all local data, including configuration, logs, cache, and exports. This action cannot be undone."),
            PrimaryButtonText = ResolveString("DataStorage_ClearAllLocalDataDialog.PrimaryButtonText", "Clear"),
            SecondaryButtonText = ResolveString("DataStorage_ClearAllLocalDataDialog.SecondaryButtonText", "Cancel"),
            DefaultButton = ContentDialogButton.Primary
        };
        ContentDialogHost.AttachToElement(dialog, this);

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            await ViewModel.ClearAllLocalDataCommand.ExecuteAsync(null);
        }
    }

    private static string ResolveString(string resourceKey, string fallback)
    {
        var value = ResourceLoader.GetString(resourceKey);
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }
}
