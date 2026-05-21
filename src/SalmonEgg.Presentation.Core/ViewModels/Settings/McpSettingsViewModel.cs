using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SalmonEgg.Domain.Models.Mcp;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Resources;

namespace SalmonEgg.Presentation.ViewModels.Settings;

public sealed partial class McpSettingsViewModel : ObservableObject
{
    private readonly IMcpSettingsService _settingsService;
    private readonly IStringLocalizer<CoreStrings> _localizer;
    private readonly ILogger<McpSettingsViewModel> _logger;

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public McpSettingsViewModel(
        IMcpSettingsService settingsService,
        IStringLocalizer<CoreStrings> localizer,
        ILogger<McpSettingsViewModel> logger)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ObservableCollection<McpServerRowViewModel> Servers { get; } = new();

    [RelayCommand]
    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (IsLoading)
        {
            return;
        }

        try
        {
            IsLoading = true;
            var settings = await _settingsService.LoadAsync(cancellationToken).ConfigureAwait(true);
            IsEnabled = settings.IsEnabled;
            Servers.Clear();
            foreach (var server in settings.Servers)
            {
                Servers.Add(AttachRowCommands(McpServerRowViewModel.FromServer(server)));
            }

            StatusMessage = string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load MCP settings");
            StatusMessage = _localizer["McpSettings_LoadFailed"];
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void AddServer()
    {
        Servers.Add(AttachRowCommands(new McpServerRowViewModel
        {
            Name = "new-mcp-server",
            Transport = McpServerTransport.Stdio
        }));
    }

    [RelayCommand]
    private void RemoveServer(McpServerRowViewModel? server)
    {
        if (server is not null)
        {
            Servers.Remove(server);
        }
    }

    private McpServerRowViewModel AttachRowCommands(McpServerRowViewModel row)
    {
        row.RemoveCommand = RemoveServerCommand;
        return row;
    }

    [RelayCommand]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            var settings = new McpSettings
            {
                IsEnabled = IsEnabled,
                Servers = Servers.Select(server => server.ToServer()).ToList()
            };

            await _settingsService.SaveAsync(settings, cancellationToken).ConfigureAwait(true);
            StatusMessage = _localizer["McpSettings_Saved"];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save MCP settings");
            StatusMessage = _localizer["McpSettings_SaveFailed"];
        }
    }
}
