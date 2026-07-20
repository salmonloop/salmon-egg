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
                title: "Clear cache",
                message: "This deletes all files in the local cache folder.",
                primaryButtonText: "Clear",
                closeButtonText: "Cancel").ConfigureAwait(true);

            if (!confirmed)
            {
                return;
            }

            await _maintenance.ClearCacheAsync().ConfigureAwait(false);
            await _ui.ShowInfoAsync("Local cache cleared.").ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ClearCache failed");
            await _ui.ShowInfoAsync("Failed to clear cache. Please try again later.").ConfigureAwait(true);
        }
    }
}
