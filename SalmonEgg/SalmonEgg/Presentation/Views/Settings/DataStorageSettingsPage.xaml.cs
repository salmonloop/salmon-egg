using Microsoft.UI.Xaml.Controls;
using System.Threading.Tasks;
using Windows.Foundation;
using SalmonEgg.Presentation.ViewModels.Settings;
using SalmonEgg.Presentation.Views;

namespace SalmonEgg.Presentation.Views.Settings;

public sealed partial class DataStorageSettingsPage : SettingsPageBase
{
    public DataStorageSettingsViewModel ViewModel { get; }

    public DataStorageSettingsPage()
    {
        ViewModel = App.ServiceProvider.GetRequiredService<DataStorageSettingsViewModel>();
        InitializeComponent();
        SetSettingsBreadcrumb("数据与存储");
    }

    private async void OnClearCacheClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "清理缓存",
            Content = "将删除本地缓存目录下的所有文件。",
            PrimaryButtonText = "清理",
            SecondaryButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary
        };

        var result = await ShowContentDialogAsync(dialog);
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
            Title = "恢复默认设置",
            Content = "将恢复常规、外观、数据与存储、快捷键等设置到默认值。",
            PrimaryButtonText = "恢复",
            SecondaryButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary
        };

        var result = await ShowContentDialogAsync(dialog);
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
            Title = "清空所有本地数据",
            Content = "这将删除所有本地数据（配置、日志、缓存、导出等）。该操作不可撤销。",
            PrimaryButtonText = "清空",
            SecondaryButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary
        };

        var result = await ShowContentDialogAsync(dialog);
        if (result == ContentDialogResult.Primary)
        {
            await ViewModel.ClearAllLocalDataCommand.ExecuteAsync(null);
        }
    }

    private static Task<ContentDialogResult> ShowContentDialogAsync(ContentDialog dialog)
    {
        var operation = dialog.ShowAsync();
        var tcs = new TaskCompletionSource<ContentDialogResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        operation.Completed = (info, status) =>
        {
            switch (status)
            {
                case AsyncStatus.Completed:
                    tcs.TrySetResult(info.GetResults());
                    break;
                case AsyncStatus.Canceled:
                    tcs.TrySetCanceled();
                    break;
                case AsyncStatus.Error:
                    tcs.TrySetException(info.ErrorCode);
                    break;
            }
        };

        return tcs.Task;
    }
}
