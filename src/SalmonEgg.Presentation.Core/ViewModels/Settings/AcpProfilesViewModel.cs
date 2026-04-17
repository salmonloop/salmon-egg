using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Services.Chat;

namespace SalmonEgg.Presentation.ViewModels.Settings;

public partial class AcpProfilesViewModel : ObservableObject, IDisposable
{
    private readonly IConfigurationService _configurationService;
    private readonly AppPreferencesViewModel _preferences;
    private readonly ILogger<AcpProfilesViewModel> _logger;
    private readonly IUiDispatcher _dispatcher;
    private readonly SemaphoreSlim _refreshSemaphore = new(1, 1);

    // ── Dependencies for per-profile item ViewModels ─────────────────────────
    // Null when the registry/events are not registered (e.g., lightweight contexts
    // such as profile editing that don't need connection status).
    private readonly IAcpConnectionSessionRegistry? _sessionRegistry;
    private readonly IAcpConnectionSessionEvents? _sessionEvents;
    private readonly ISettingsAcpConnectionCommands? _connectionCommands;
    private readonly ILoggerFactory? _loggerFactory;

    // ── Existing contract (kept for backward-compat with ChatViewModel etc.) ──

    [ObservableProperty]
    private ObservableCollection<ServerConfiguration> _profiles = new();

    [ObservableProperty]
    private ServerConfiguration? _selectedProfile;

    [ObservableProperty]
    private AgentProfileItemViewModel? _selectedProfileItem;

    [ObservableProperty]
    private bool _isLoading;

    partial void OnSelectedProfileItemChanged(AgentProfileItemViewModel? value)
    {
        if (value != null)
        {
            var profile = Profiles.FirstOrDefault(p => p.Id == value.ProfileId);
            if (SelectedProfile != profile)
            {
                SelectedProfile = profile;
            }
        }
        else
        {
            SelectedProfile = null;
        }
    }

    // ── New: per-profile item VMs for the Settings card list ─────────────────

    /// <summary>
    /// One <see cref="AgentProfileItemViewModel"/> per profile, each carrying its own
    /// connection state. Bound by <c>AcpConnectionSettingsPage</c> instead of raw
    /// <see cref="Profiles"/>. Populated by <see cref="RefreshAsync"/>.
    /// </summary>
    public ObservableCollection<AgentProfileItemViewModel> ProfileItems { get; } = new();

    // ── Constructors ──────────────────────────────────────────────────────────

    /// <summary>
    /// Minimal constructor (no connection-state support). Used in scenarios that only
    /// need profile CRUD without live status (e.g., DiscoverSessionsViewModel).
    /// </summary>
    public AcpProfilesViewModel(
        IConfigurationService configurationService,
        AppPreferencesViewModel preferences,
        ILogger<AcpProfilesViewModel> logger,
        IUiDispatcher dispatcher)
    {
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    /// <summary>
    /// Full constructor with connection-state support. Used by the Settings page.
    /// </summary>
    public AcpProfilesViewModel(
        IConfigurationService configurationService,
        AppPreferencesViewModel preferences,
        ILogger<AcpProfilesViewModel> logger,
        IAcpConnectionSessionRegistry sessionRegistry,
        IAcpConnectionSessionEvents sessionEvents,
        ISettingsAcpConnectionCommands connectionCommands,
        ILoggerFactory loggerFactory,
        IUiDispatcher dispatcher)
        : this(configurationService, preferences, logger, dispatcher)
    {
        _sessionRegistry = sessionRegistry ?? throw new ArgumentNullException(nameof(sessionRegistry));
        _sessionEvents = sessionEvents ?? throw new ArgumentNullException(nameof(sessionEvents));
        _connectionCommands = connectionCommands ?? throw new ArgumentNullException(nameof(connectionCommands));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    // ── Public helpers (unchanged contract) ───────────────────────────────────

    public async Task RefreshIfEmptyAsync()
    {
        if (Profiles.Count == 0 && !IsLoading)
        {
            await RefreshAsync().ConfigureAwait(false);
        }
    }

    private Task MarshalToUiAsync(Action action)
    {
        return _dispatcher.EnqueueAsync(action);
    }

    private Task MarshalToUiAsync(Func<Task> function)
    {
        return _dispatcher.EnqueueAsync(function);
    }

    private Task SetIsLoadingAsync(bool value)
    {
        return MarshalToUiAsync(() => IsLoading = value);
    }

    public void MarkLastConnected(ServerConfiguration? profile)
    {
        _preferences.LastSelectedServerId = profile?.Id;
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand]
    public async Task RefreshAsync()
    {
        // Use semaphore to ensure mutual exclusivity and thread-safe check of loading state.
        if (!await _refreshSemaphore.WaitAsync(0).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            await SetIsLoadingAsync(true).ConfigureAwait(false);
            
            var configs = await _configurationService.ListConfigurationsAsync().ConfigureAwait(false);
            var ordered = configs.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToArray();

            await MarshalToUiAsync(() =>
            {
                // ── Update legacy flat list (backward compat) ────────────
                Profiles.Clear();
                foreach (var cfg in ordered)
                {
                    Profiles.Add(cfg);
                }

                if (!string.IsNullOrWhiteSpace(_preferences.LastSelectedServerId))
                {
                    SelectedProfile = Profiles.FirstOrDefault(p => p.Id == _preferences.LastSelectedServerId);
                }

                // ── Rebuild per-profile item VMs ─────────────────────────
                RebuildProfileItems(ordered);
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh server profiles");
        }
        finally
        {
            await SetIsLoadingAsync(false).ConfigureAwait(false);
            _refreshSemaphore.Release();
        }
    }

    [RelayCommand]
    public async Task DeleteAsync(ServerConfiguration? profile)
    {
        if (profile == null)
        {
            return;
        }

        try
        {
            await _configurationService.DeleteConfigurationAsync(profile.Id).ConfigureAwait(false);

            await MarshalToUiAsync(() =>
            {
                Profiles.Remove(profile);
                if (SelectedProfile?.Id == profile.Id)
                {
                    SelectedProfile = null;
                }

                // Remove the matching item VM.
                var item = ProfileItems.FirstOrDefault(vm => vm.ProfileId == profile.Id);
                if (item != null)
                {
                    ProfileItems.Remove(item);
                    item.Dispose();
                }
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete server profile {ProfileId}", profile.Id);
        }
    }

    public async Task SaveAsync(ServerConfiguration profile)
    {
        if (profile == null)
        {
            return;
        }

        try
        {
            await _configurationService.SaveConfigurationAsync(profile).ConfigureAwait(false);
            await RefreshAsync().ConfigureAwait(false);
            SelectById(profile.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save server profile {ProfileId}", profile.Id);
        }
    }

    public async Task SaveNewAsync(ServerConfiguration profile)
    {
        if (profile == null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(profile.Id))
        {
            profile.Id = Guid.NewGuid().ToString();
        }

        await SaveAsync(profile).ConfigureAwait(false);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Rebuilds <see cref="ProfileItems"/> to match <paramref name="ordered"/>,
    /// reusing existing item VMs where possible to preserve live state.
    /// Must be called on the UI thread.
    /// </summary>
    private void RebuildProfileItems(ServerConfiguration[] ordered)
    {
        // Dispose item VMs that no longer have a corresponding config.
        var toRemove = ProfileItems
            .Where(vm => ordered.All(cfg => cfg.Id != vm.ProfileId))
            .ToArray();

        foreach (var vm in toRemove)
        {
            ProfileItems.Remove(vm);
            vm.Dispose();
        }

        // Add or update item VMs in order.
        for (int i = 0; i < ordered.Length; i++)
        {
            var config = ordered[i];
            var existing = ProfileItems.FirstOrDefault(vm => vm.ProfileId == config.Id);

            if (existing == null)
            {
                var itemVm = CreateProfileItemViewModel(config);
                if (itemVm != null)
                {
                    ProfileItems.Insert(i, itemVm);
                }
            }
            // Note: we don't re-order existing VMs for performance (a future enhancement).
        }
    }

    private AgentProfileItemViewModel? CreateProfileItemViewModel(ServerConfiguration config)
    {
        // Only create item VMs when the connection dependencies were provided.
        if (_sessionRegistry == null || _sessionEvents == null ||
            _connectionCommands == null || _loggerFactory == null)
        {
            return null;
        }

        return new AgentProfileItemViewModel(
            config,
            _sessionRegistry,
            _sessionEvents,
            _connectionCommands,
            _loggerFactory.CreateLogger<AgentProfileItemViewModel>());
    }

    private void SelectById(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        _ = MarshalToUiAsync(() =>
        {
            var profile = Profiles.FirstOrDefault(p => p.Id == id);
            SelectedProfile = profile;
            SelectedProfileItem = ProfileItems.FirstOrDefault(vm => vm.ProfileId == id);
        });
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    public void Dispose()
    {
        foreach (var vm in ProfileItems)
        {
            vm.Dispose();
        }
    }
}
