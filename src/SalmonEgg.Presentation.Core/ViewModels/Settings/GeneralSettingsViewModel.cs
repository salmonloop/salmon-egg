using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Resources;
using SalmonEgg.Presentation.Services;

namespace SalmonEgg.Presentation.ViewModels.Settings;

public sealed partial class GeneralSettingsViewModel : ObservableObject
{
    private readonly IAppMaintenanceService _maintenance;
    private readonly IUiInteractionService _ui;
    private readonly IStringLocalizer<CoreStrings> _localizer;
    private readonly ILogger<GeneralSettingsViewModel> _logger;

    public AppPreferencesViewModel Preferences { get; }

    public GeneralSettingsViewModel(
        AppPreferencesViewModel preferences,
        IAppMaintenanceService maintenance,
        IUiInteractionService ui,
        IStringLocalizer<CoreStrings> localizer,
        ILogger<GeneralSettingsViewModel> logger)
    {
        Preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _maintenance = maintenance ?? throw new ArgumentNullException(nameof(maintenance));
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [RelayCommand]
    private async Task ClearCacheAsync()
    {
        try
        {
            var confirmed = await _ui.ConfirmAsync(
                title: _localizer["General_ClearCacheTitle"],
                message: _localizer["General_ClearCacheMessage"],
                primaryButtonText: _localizer["General_ClearCachePrimary"],
                closeButtonText: _localizer["Common_Cancel"]).ConfigureAwait(true);

            if (!confirmed)
            {
                return;
            }

            await _maintenance.ClearCacheAsync().ConfigureAwait(false);
            await _ui.ShowInfoAsync(_localizer["General_ClearCacheSuccess"]).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ClearCache failed");
            await _ui.ShowInfoAsync(_localizer["General_ClearCacheFailed"]).ConfigureAwait(true);
        }
    }
}
