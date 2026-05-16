using Microsoft.UI.Xaml.Controls;
using SalmonEgg.Presentation.ViewModels.Settings;
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
        SetSettingsBreadcrumbFromResource("SettingsNav_DataStorage.Content", "数据与存储");
    }

    private async void OnClearCacheClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = ResolveString("DataStorage_ClearCacheDialog.Title", "清理缓存"),
            Content = ResolveString("DataStorage_ClearCacheDialog.Content", "将删除本地缓存目录下的所有文件。"),
            PrimaryButtonText = ResolveString("DataStorage_ClearCacheDialog.PrimaryButtonText", "清理"),
            SecondaryButtonText = ResolveString("DataStorage_ClearCacheDialog.SecondaryButtonText", "取消"),
            DefaultButton = ContentDialogButton.Primary
        };

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
            XamlRoot = XamlRoot,
            Title = ResolveString("DataStorage_ResetPreferencesDialog.Title", "恢复默认设置"),
            Content = ResolveString("DataStorage_ResetPreferencesDialog.Content", "将恢复常规、外观、数据与存储、快捷键等设置到默认值。"),
            PrimaryButtonText = ResolveString("DataStorage_ResetPreferencesDialog.PrimaryButtonText", "恢复"),
            SecondaryButtonText = ResolveString("DataStorage_ResetPreferencesDialog.SecondaryButtonText", "取消"),
            DefaultButton = ContentDialogButton.Primary
        };

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
            XamlRoot = XamlRoot,
            Title = ResolveString("DataStorage_ClearAllLocalDataDialog.Title", "清空所有本地数据"),
            Content = ResolveString("DataStorage_ClearAllLocalDataDialog.Content", "这将删除所有本地数据（配置、日志、缓存、导出等）。该操作不可撤销。"),
            PrimaryButtonText = ResolveString("DataStorage_ClearAllLocalDataDialog.PrimaryButtonText", "清空"),
            SecondaryButtonText = ResolveString("DataStorage_ClearAllLocalDataDialog.SecondaryButtonText", "取消"),
            DefaultButton = ContentDialogButton.Primary
        };

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
