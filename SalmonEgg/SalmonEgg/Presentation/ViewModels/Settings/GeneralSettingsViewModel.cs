using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Services;

namespace SalmonEgg.Presentation.ViewModels.Settings;

public sealed partial class GeneralSettingsViewModel : ObservableObject
{
    private readonly IAppMaintenanceService _maintenance;
    private readonly IUiInteractionService _ui;
    private readonly ILogger<GeneralSettingsViewModel> _logger;

    public AppPreferencesViewModel Preferences { get; }

    public GeneralSettingsViewModel(
        AppPreferencesViewModel preferences,
        IAppMaintenanceService maintenance,
        IUiInteractionService ui,
        ILogger<GeneralSettingsViewModel> logger)
    {
        Preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _maintenance = maintenance ?? throw new ArgumentNullException(nameof(maintenance));
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [RelayCommand]
    private async Task ClearCacheAsync()
    {
        try
        {
            var confirmed = await _ui.ConfirmAsync(
                title: "清理缓存",
                message: "将删除本地缓存目录下的所有文件。",
                primaryButtonText: "清理",
                closeButtonText: "取消").ConfigureAwait(true);

            if (!confirmed)
            {
                return;
            }

            await _maintenance.ClearCacheAsync().ConfigureAwait(false);
            await _ui.ShowInfoAsync("已清理本地缓存。").ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ClearCache failed");
            await _ui.ShowInfoAsync("清理缓存失败，请稍后重试。").ConfigureAwait(true);
        }
    }
}
