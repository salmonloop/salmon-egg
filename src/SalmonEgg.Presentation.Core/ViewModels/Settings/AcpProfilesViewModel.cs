using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Resources;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Services.Chat;

namespace SalmonEgg.Presentation.ViewModels.Settings;

public partial class AcpProfilesViewModel : ObservableObject, IDisposable
{
    private readonly IConfigurationService _configurationService;
    private readonly AppPreferencesViewModel _preferences;
    private readonly ILogger<AcpProfilesViewModel> _logger;
    private readonly IUiDispatcher _dispatcher;
    private readonly IStringLocalizer<CoreStrings>? _localizer;
    private readonly IAppLanguageService? _languageService;
    private readonly SemaphoreSlim _refreshSemaphore = new(1, 1);
    private bool _disposed;

    // ── Dependencies for per-profile item ViewModels ─────────────────────────
    // Null when the registry/events are not registered (e.g., lightweight contexts
    // such as profile editing that don't need connection status).
    private readonly IAcpConnectionSessionRegistry? _sessionRegistry;
    private readonly IAcpConnectionSessionEvents? _sessionEvents;
    private readonly ISettingsAcpConnectionCommands? _connectionCommands;
    private readonly ILoggerFactory? _loggerFactory;

    // Raw profile projection shared by Chat, Discover, and Settings editors.

    [ObservableProperty]
    private ObservableCollection<ServerConfiguration> _profiles = new();

    private string? _selectedProfileId;
    private ServerConfiguration? _selectedProfileSnapshot;

    public string? SelectedProfileId
    {
        get => _selectedProfileId;
        set => SetSelectedProfileId(value);
    }

    public ServerConfiguration? SelectedProfile
    {
        get => ResolveSelectedProfile();
        set => SetSelectedProfileId(value?.Id, value);
    }

    public AgentProfileItemViewModel? SelectedProfileItem
    {
        get => ResolveSelectedProfileItem();
        set => SetSelectedProfileId(value?.ProfileId);
    }

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isSavedCurrentConnectionNoticeOpen;

    [ObservableProperty]
    private string _savedCurrentConnectionNoticeMessage = string.Empty;

    [ObservableProperty]
    private bool _isOperationErrorOpen;

    [ObservableProperty]
    private string _operationErrorMessage = string.Empty;

    private string? _operationErrorResourceKey;

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
        IUiDispatcher dispatcher,
        IStringLocalizer<CoreStrings>? localizer = null,
        IAppLanguageService? languageService = null)
    {
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _localizer = localizer;
        _languageService = languageService;
        if (_languageService is not null)
        {
            _languageService.LanguageChanged += OnLanguageChanged;
        }
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
        IUiDispatcher dispatcher,
        IStringLocalizer<CoreStrings> localizer,
        IAppLanguageService? languageService = null)
        : this(configurationService, preferences, logger, dispatcher, localizer, languageService)
    {
        _sessionRegistry = sessionRegistry ?? throw new ArgumentNullException(nameof(sessionRegistry));
        _sessionEvents = sessionEvents ?? throw new ArgumentNullException(nameof(sessionEvents));
        _connectionCommands = connectionCommands ?? throw new ArgumentNullException(nameof(connectionCommands));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    public async Task RefreshIfEmptyAsync()
    {
        if (Profiles.Count == 0)
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

    public void ShowSavedCurrentConnectionNoticeIfNeeded(string? profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            DismissSavedCurrentConnectionNotice();
            return;
        }

        var item = ProfileItems.FirstOrDefault(vm => string.Equals(vm.ProfileId, profileId, StringComparison.Ordinal));
        if (item?.IsConnected != true)
        {
            DismissSavedCurrentConnectionNotice();
            return;
        }

        SavedCurrentConnectionNoticeMessage = Localize(
            CurrentConnectionSavedNoticeMessageKey,
            CurrentConnectionSavedNoticeMessageFallback);
        IsSavedCurrentConnectionNoticeOpen = true;
    }

    public void DismissSavedCurrentConnectionNotice()
    {
        IsSavedCurrentConnectionNoticeOpen = false;
        SavedCurrentConnectionNoticeMessage = string.Empty;
    }

    public void DismissOperationError()
    {
        IsOperationErrorOpen = false;
        OperationErrorMessage = string.Empty;
        _operationErrorResourceKey = null;
    }

    private Task ShowOperationErrorAsync(string resourceKey, string fallback)
    {
        return MarshalToUiAsync(() =>
        {
            _operationErrorResourceKey = resourceKey;
            OperationErrorMessage = Localize(resourceKey, fallback);
            IsOperationErrorOpen = true;
        });
    }

    public void ReportOperationError(string resourceKey, string fallback)
    {
        // Shared Error InfoBar entry point for profile/connection failures that do not own
        // their own dialog chrome (item commands and legacy settings connection helpers).
        _ = ShowOperationErrorAsync(resourceKey, fallback);
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand]
    public async Task RefreshAsync()
    {
        await _refreshSemaphore.WaitAsync().ConfigureAwait(false);

        try
        {
            await SetIsLoadingAsync(true).ConfigureAwait(false);

            var configs = await _configurationService.ListConfigurationsAsync().ConfigureAwait(false);
            var ordered = configs.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToArray();

            await MarshalToUiAsync(() =>
            {
                DismissOperationError();
                var preferredSelectedProfileId = SelectedProfileId ?? _preferences.LastSelectedServerId;

                RebuildProfileItems(ordered);

                Profiles.Clear();
                foreach (var cfg in ordered)
                {
                    Profiles.Add(cfg);
                }

                var nextSelectedProfileId = ResolveAvailableProfileId(preferredSelectedProfileId);
                if (!SetSelectedProfileId(nextSelectedProfileId))
                {
                    NotifySelectedProfileProjectionChanged();
                }
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh server profiles");
            await ShowOperationErrorAsync(
                "AcpProfiles_RefreshFailed",
                "Failed to refresh agent profiles. Please try again later.").ConfigureAwait(false);
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

                // Remove the matching item VM.
                var item = ProfileItems.FirstOrDefault(vm => vm.ProfileId == profile.Id);
                if (item != null)
                {
                    ProfileItems.Remove(item);
                    item.Dispose();
                }

                if (string.Equals(SelectedProfileId, profile.Id, StringComparison.Ordinal))
                {
                    SetSelectedProfileId(null);
                }
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete server profile {ProfileId}", profile.Id);
            await ShowOperationErrorAsync(
                "AcpProfiles_DeleteFailed",
                "Failed to delete the agent profile. Please try again later.").ConfigureAwait(false);
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
            await SelectByIdAsync(profile.Id).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save server profile {ProfileId}", profile.Id);
            await ShowOperationErrorAsync(
                "AcpProfiles_SaveFailed",
                "Failed to save the agent profile. Please try again later.").ConfigureAwait(false);
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
            else
            {
                existing.UpdateProfile(config);
                var currentIndex = ProfileItems.IndexOf(existing);
                if (currentIndex >= 0 && currentIndex != i)
                {
                    ProfileItems.Move(currentIndex, i);
                }
            }
        }
    }

    private AgentProfileItemViewModel? CreateProfileItemViewModel(ServerConfiguration config)
    {
        // Only create item VMs when the connection dependencies were provided.
        if (_sessionRegistry == null || _sessionEvents == null ||
            _connectionCommands == null || _loggerFactory == null ||
            _localizer == null)
        {
            return null;
        }

        return new AgentProfileItemViewModel(
            config,
            _sessionRegistry,
            _sessionEvents,
            _connectionCommands,
            _loggerFactory.CreateLogger<AgentProfileItemViewModel>(),
            _dispatcher,
            _localizer,
            ReportOperationError);
    }

    private Task SelectByIdAsync(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return Task.CompletedTask;
        }

        return MarshalToUiAsync(() => SetSelectedProfileId(ResolveAvailableProfileId(id)));
    }

    private ServerConfiguration? ResolveSelectedProfile()
    {
        if (string.IsNullOrWhiteSpace(SelectedProfileId))
        {
            return null;
        }

        return ResolveProfileById(SelectedProfileId)
            ?? (IsProfileSnapshotForId(_selectedProfileSnapshot, SelectedProfileId) ? _selectedProfileSnapshot : null);
    }

    private AgentProfileItemViewModel? ResolveSelectedProfileItem()
        => string.IsNullOrWhiteSpace(SelectedProfileId)
            ? null
            : ProfileItems.FirstOrDefault(item => string.Equals(item.ProfileId, SelectedProfileId, StringComparison.Ordinal));

    private string? ResolveAvailableProfileId(string? profileId)
        => string.IsNullOrWhiteSpace(profileId)
            ? null
            : Profiles.Any(profile => string.Equals(profile.Id, profileId, StringComparison.Ordinal))
                ? profileId
                : null;

    private bool SetSelectedProfileId(string? profileId, ServerConfiguration? profileSnapshot = null)
    {
        var normalized = string.IsNullOrWhiteSpace(profileId) ? null : profileId;
        var nextSnapshot = ResolveProfileById(normalized)
            ?? (IsProfileSnapshotForId(profileSnapshot, normalized) ? profileSnapshot : null);
        var idChanged = !string.Equals(_selectedProfileId, normalized, StringComparison.Ordinal);
        var snapshotChanged = !ReferenceEquals(_selectedProfileSnapshot, nextSnapshot);
        if (!idChanged && !snapshotChanged)
        {
            return false;
        }

        _selectedProfileId = normalized;
        _selectedProfileSnapshot = nextSnapshot;
        if (idChanged)
        {
            OnPropertyChanged(nameof(SelectedProfileId));
        }

        NotifySelectedProfileProjectionChanged();
        return true;
    }

    private ServerConfiguration? ResolveProfileById(string? profileId)
        => string.IsNullOrWhiteSpace(profileId)
            ? null
            : Profiles.FirstOrDefault(profile => string.Equals(profile.Id, profileId, StringComparison.Ordinal));

    private static bool IsProfileSnapshotForId(ServerConfiguration? profile, string? profileId)
        => profile is not null
            && !string.IsNullOrWhiteSpace(profileId)
            && string.Equals(profile.Id, profileId, StringComparison.Ordinal);

    private void NotifySelectedProfileProjectionChanged()
    {
        OnPropertyChanged(nameof(SelectedProfile));
        OnPropertyChanged(nameof(SelectedProfileItem));
    }

    private const string CurrentConnectionSavedNoticeMessageKey =
        "AgentProfileEditor_CurrentConnectionSavedNoticeMessage";

    private const string CurrentConnectionSavedNoticeMessageFallback =
        "Settings saved. The current connection still uses the old configuration until reconnect.";

    private string Localize(string key, string fallback)
    {
        if (_localizer is null)
        {
            return fallback;
        }

        var localized = _localizer[key];
        return localized.ResourceNotFound || string.IsNullOrWhiteSpace(localized.Value)
            ? fallback
            : localized.Value;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
        => _ = MarshalToUiAsync(ReprojectLocalizedState);

    private void ReprojectLocalizedState()
    {
        if (IsSavedCurrentConnectionNoticeOpen)
        {
            SavedCurrentConnectionNoticeMessage = Localize(
                CurrentConnectionSavedNoticeMessageKey,
                CurrentConnectionSavedNoticeMessageFallback);
        }

        if (IsOperationErrorOpen && !string.IsNullOrWhiteSpace(_operationErrorResourceKey))
        {
            OperationErrorMessage = Localize(_operationErrorResourceKey, OperationErrorMessage);
        }

        foreach (var item in ProfileItems)
        {
            item.ReprojectLocalizedState();
        }
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_languageService is not null)
        {
            _languageService.LanguageChanged -= OnLanguageChanged;
        }

        foreach (var vm in ProfileItems)
        {
            vm.Dispose();
        }
    }
}
