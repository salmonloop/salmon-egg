using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SalmonEgg.Domain.Models;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Resources;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Services;

namespace SalmonEgg.Presentation.ViewModels.Settings;

public partial class AppPreferencesViewModel : ObservableObject
{
    private readonly IAppSettingsService _appSettingsService;
    private readonly IAppStartupService _startupService;
    private readonly IAppLanguageService _languageService;
    private readonly IPlatformCapabilityService _capabilities;
    private readonly IUiRuntimeService _uiRuntime;
    private readonly IUiInteractionService _ui;
    private readonly IStringLocalizer<CoreStrings> _localizer;
    private readonly ILogger<AppPreferencesViewModel> _logger;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly SemaphoreSlim _initializeGate = new(1, 1);
    private CancellationTokenSource? _saveCts;
    private bool _isInitialized;
    private bool _suppressSave;
    private string _appliedLanguage = "System";

    [ObservableProperty]
    private string _theme = "System";

    [ObservableProperty]
    private bool _isAnimationEnabled = true;

    [ObservableProperty]
    private string _backdrop = "System";

    [ObservableProperty]
    private bool _launchOnStartup;

    [ObservableProperty]
    private bool _minimizeToTray = true;

    [ObservableProperty]
    private string _language = "System";

    [ObservableProperty]
    private string? _lastSelectedServerId;

    [ObservableProperty]
    private bool _saveLocalHistory = true;

    [ObservableProperty]
    private int _cacheRetentionDays = 7;

    // Telemetry：默认开启（opt-out 模式，与 VS Code / Firefox 一致）。
    // 修改在设置保存成功后立即重建遥测管线；ViewModel 只表达用户意图，
    // provider 生命周期由 Infrastructure 的重配置服务拥有。
    [ObservableProperty]
    private bool _telemetrySharingEnabled = true;

    [ObservableProperty]
    private string? _telemetryCustomEndpoint;

    [ObservableProperty]
    private string? _telemetryAuthHeader;

    [ObservableProperty]
    private CloudConfigSyncSettings _cloudConfigSync = new();

    // Navigation state (Projects -> Sessions) lives in AppSettings to persist between launches.
    // Keep a single writer (AppPreferencesViewModel) to avoid settings overwrite races.
    public ObservableCollection<ProjectDefinition> Projects { get; } = new();

    public ObservableCollection<AgentRemoteDirectory> AgentRemoteDirectories { get; } = new();

    // Reference-only membership: which configured remote directories the user added to the
    // navigation project list. Stores stable DirectoryId values only; AgentRemoteDirectories
    // remains the single source of truth for display name and remote path.
    public ObservableCollection<string> NavigationRemoteDirectoryIds { get; } = new();

    [ObservableProperty]
    private string? _lastSelectedProjectId;

    // ACP connection governance (advanced, currently no direct UI editor).
    [ObservableProperty]
    private bool _acpEnableConnectionEviction;

    [ObservableProperty]
    private int? _acpConnectionIdleTtlMinutes;

    [ObservableProperty]
    private int? _acpMaxWarmProfiles;

    [ObservableProperty]
    private int? _acpMaxPinnedProfiles;

    [ObservableProperty]
    private string _acpHydrationCompletionMode = "StrictReplay";

    [ObservableProperty]
    private bool _isLoaded;

    [ObservableProperty]
    private bool _keyboardShortcutsEnabled = true;

    public ObservableCollection<KeyBindingPairViewModel> KeyBindings { get; } = new();
    public ObservableCollection<AppLanguageOptionViewModel> LanguageOptions { get; } = CreateLanguageOptions();
    public ObservableCollection<SettingsOptionViewModel> ThemeOptions { get; } = CreateThemeOptions();
    public ObservableCollection<SettingsOptionViewModel> BackdropOptions { get; } = CreateBackdropOptions();
    public event EventHandler? ShortcutConfigurationChanged;

    [ObservableProperty]
    private AppLanguageOptionViewModel? _selectedLanguageOption;

    [ObservableProperty]
    private SettingsOptionViewModel? _selectedThemeOption;

    [ObservableProperty]
    private SettingsOptionViewModel? _selectedBackdropOption;

    public bool IsLaunchOnStartupSupported => _capabilities.SupportsLaunchOnStartup;

    public bool IsMinimizeToTraySupported => _capabilities.SupportsTray;

    public bool IsLanguageOverrideSupported => _capabilities.SupportsLanguageOverride;

    public bool IsMiniWindowSupported => _capabilities.SupportsMiniWindow;

    public bool IsStdioTransportSupported => _capabilities.SupportsStdioTransport;

    public bool IsLocalTerminalSupported => _capabilities.SupportsLocalTerminal;

    internal IPlatformCapabilityService PlatformCapabilities => _capabilities;

    public AppPreferencesViewModel(
        IAppSettingsService appSettingsService,
        IAppStartupService startupService,
        IAppLanguageService languageService,
        IPlatformCapabilityService capabilities,
        IUiRuntimeService uiRuntime,
        IUiInteractionService ui,
        IStringLocalizer<CoreStrings> localizer,
        ILogger<AppPreferencesViewModel> logger,
        IUiDispatcher uiDispatcher)
    {
        _appSettingsService = appSettingsService ?? throw new ArgumentNullException(nameof(appSettingsService));
        _startupService = startupService ?? throw new ArgumentNullException(nameof(startupService));
        _languageService = languageService ?? throw new ArgumentNullException(nameof(languageService));
        _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        _uiRuntime = uiRuntime ?? throw new ArgumentNullException(nameof(uiRuntime));
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
        KeyBindings.CollectionChanged += OnKeyBindingsChanged;
        Projects.CollectionChanged += OnProjectsChanged;
        AgentRemoteDirectories.CollectionChanged += OnAgentRemoteDirectoriesChanged;
        NavigationRemoteDirectoryIds.CollectionChanged += OnNavigationRemoteDirectoryIdsChanged;
        SelectedLanguageOption = ResolveLanguageOption(Language);
        SelectedThemeOption = ResolveSettingsOption(ThemeOptions, Theme, "System");
        SelectedBackdropOption = ResolveSettingsOption(BackdropOptions, Backdrop, "System");
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_isInitialized)
        {
            return;
        }

        await _initializeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_isInitialized)
            {
                return;
            }

            await LoadAsync(cancellationToken, reloadShellOnLanguageChange: false).ConfigureAwait(false);
            _isInitialized = true;
        }
        finally
        {
            _initializeGate.Release();
        }
    }

    public async Task ReloadFromStoreAsync(CancellationToken cancellationToken = default)
    {
        await _initializeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var wasInitialized = _isInitialized;
            CancelPendingSave();
            await LoadAsync(cancellationToken, reloadShellOnLanguageChange: wasInitialized).ConfigureAwait(false);
            _isInitialized = true;
        }
        finally
        {
            _initializeGate.Release();
        }
    }

    private async Task LoadAsync(CancellationToken cancellationToken, bool reloadShellOnLanguageChange)
    {
        var markLoaded = true;
        var previousLanguage = "System";
        var nextLanguage = "System";
#if DEBUG
        _logger.LogInformation(
            "App settings load started. ReloadShellOnLanguageChange={ReloadShellOnLanguageChange} IsInitialized={IsInitialized} IsLoaded={IsLoaded} CurrentLanguage={CurrentLanguage} AppliedLanguage={AppliedLanguage}",
            reloadShellOnLanguageChange,
            _isInitialized,
            IsLoaded,
            Language,
            _appliedLanguage);
#endif
        try
        {
            _suppressSave = true;
            cancellationToken.ThrowIfCancellationRequested();
            var settings = await _appSettingsService.LoadAsync();
            var launchOnStartup = settings.LaunchOnStartup;
#if DEBUG
            _logger.LogInformation(
                "App settings file loaded. SettingsLanguage={SettingsLanguage} SettingsTheme={SettingsTheme} LastSelectedServerId={LastSelectedServerId} ProjectCount={ProjectCount} RemoteDirectoryCount={RemoteDirectoryCount}",
                settings.Language,
                settings.Theme,
                settings.LastSelectedServerId,
                settings.Projects.Count,
                settings.AgentRemoteDirectories.Count);
#endif

            try
            {
                var state = await _startupService.GetLaunchOnStartupAsync().ConfigureAwait(false);
                if (state.HasValue)
                {
                    launchOnStartup = state.Value;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to query launch-on-startup state");
            }

            await _uiDispatcher.EnqueueAsync(() =>
            {
                previousLanguage = AppLanguageCatalog.NormalizeTag(Language);
                nextLanguage = AppLanguageCatalog.NormalizeTag(settings.Language);
                Theme = settings.Theme;
                IsAnimationEnabled = settings.IsAnimationEnabled;
                Backdrop = settings.Backdrop;
                LaunchOnStartup = launchOnStartup;
                MinimizeToTray = settings.MinimizeToTray;
                Language = nextLanguage;
                _appliedLanguage = nextLanguage;
                LastSelectedServerId = settings.LastSelectedServerId;
                SaveLocalHistory = settings.SaveLocalHistory;
                CacheRetentionDays = settings.CacheRetentionDays;
                TelemetrySharingEnabled = settings.TelemetrySharingEnabled;
                TelemetryCustomEndpoint = settings.TelemetryCustomEndpoint;
                TelemetryAuthHeader = settings.TelemetryAuthHeader;
                CloudConfigSync = CloneCloudConfigSyncSettings(settings.CloudConfigSync);
                KeyboardShortcutsEnabled = settings.KeyboardShortcutsEnabled;
                LastSelectedProjectId = settings.LastSelectedProjectId;
                AcpEnableConnectionEviction = settings.AcpEnableConnectionEviction;
                AcpConnectionIdleTtlMinutes = settings.AcpConnectionIdleTtlMinutes;
                AcpMaxWarmProfiles = settings.AcpMaxWarmProfiles;
                AcpMaxPinnedProfiles = settings.AcpMaxPinnedProfiles;
                AcpHydrationCompletionMode = string.IsNullOrWhiteSpace(settings.AcpHydrationCompletionMode)
                    ? "StrictReplay"
                    : settings.AcpHydrationCompletionMode.Trim();

                Projects.Clear();
                foreach (var project in settings.Projects)
                {
                    if (project is null)
                    {
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(project.ProjectId) ||
                        string.IsNullOrWhiteSpace(project.Name) ||
                        string.IsNullOrWhiteSpace(project.RootPath))
                    {
                        continue;
                    }

                    Projects.Add(new ProjectDefinition
                    {
                        ProjectId = project.ProjectId.Trim(),
                        Name = project.Name.Trim(),
                        RootPath = project.RootPath.Trim()
                    });
                }

                AgentRemoteDirectories.Clear();
                foreach (var directory in NormalizeAgentRemoteDirectories(settings.AgentRemoteDirectories))
                {
                    AgentRemoteDirectories.Add(directory);
                }

                NavigationRemoteDirectoryIds.Clear();
                foreach (var membershipId in NormalizeNavigationRemoteDirectoryIds(
                             settings.NavigationRemoteDirectoryIds,
                             AgentRemoteDirectories))
                {
                    NavigationRemoteDirectoryIds.Add(membershipId);
                }

                KeyBindings.Clear();
                foreach (var kvp in settings.KeyBindings)
                {
                    KeyBindings.Add(new KeyBindingPairViewModel(kvp.Key, kvp.Value));
                }
            }).ConfigureAwait(false);
#if DEBUG
            _logger.LogInformation(
                "App settings UI projection completed. PreviousLanguage={PreviousLanguage} NextLanguage={NextLanguage} ProjectCount={ProjectCount} RemoteDirectoryCount={RemoteDirectoryCount} NavigationRemoteDirectoryIdCount={NavigationRemoteDirectoryIdCount} KeyBindingCount={KeyBindingCount}",
                previousLanguage,
                nextLanguage,
                Projects.Count,
                AgentRemoteDirectories.Count,
                NavigationRemoteDirectoryIds.Count,
                KeyBindings.Count);
#endif

            cancellationToken.ThrowIfCancellationRequested();
#if DEBUG
            _logger.LogInformation(
                "Applying language override from app settings. PreviousLanguage={PreviousLanguage} NextLanguage={NextLanguage} ReloadShellOnLanguageChange={ReloadShellOnLanguageChange}",
                previousLanguage,
                nextLanguage,
                reloadShellOnLanguageChange);
#endif
            await _languageService.ApplyLanguageOverrideAsync(nextLanguage).ConfigureAwait(false);
#if DEBUG
            _logger.LogInformation(
                "Language override from app settings completed. PreviousLanguage={PreviousLanguage} NextLanguage={NextLanguage}",
                previousLanguage,
                nextLanguage);
#endif
            if (reloadShellOnLanguageChange &&
                !string.Equals(previousLanguage, nextLanguage, StringComparison.Ordinal))
            {
#if DEBUG
                _logger.LogInformation(
                    "Reloading shell after app settings language change. PreviousLanguage={PreviousLanguage} NextLanguage={NextLanguage}",
                    previousLanguage,
                    nextLanguage);
#endif
                _uiRuntime.ReloadShell();
            }
        }
        catch (OperationCanceledException)
        {
            markLoaded = false;
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load app settings");
#if DEBUG
            _logger.LogInformation(
                "Surfacing app settings load failure. MarkLoaded={MarkLoaded} PreviousLanguage={PreviousLanguage} NextLanguage={NextLanguage}",
                markLoaded,
                previousLanguage,
                nextLanguage);
#endif
            try
            {
                await _ui.ShowInfoAsync(_localizer["General_AppSettingsLoadFailed"]).ConfigureAwait(false);
            }
            catch (Exception showEx)
            {
                _logger.LogWarning(showEx, "Failed to surface app settings load failure");
            }
        }
        finally
        {
            await _uiDispatcher.EnqueueAsync(() =>
            {
                if (markLoaded)
                {
                    IsLoaded = true;
                }

                _suppressSave = false;
#if DEBUG
                _logger.LogInformation(
                    "App settings load finalized. MarkLoaded={MarkLoaded} IsLoaded={IsLoaded} SuppressSave={SuppressSave}",
                    markLoaded,
                    IsLoaded,
                    _suppressSave);
#endif
            }).ConfigureAwait(false);
        }
    }

    partial void OnThemeChanged(string value)
    {
        var option = ResolveSettingsOption(ThemeOptions, value, "System");
        if (!ReferenceEquals(SelectedThemeOption, option))
        {
            SelectedThemeOption = option;
        }

        ScheduleSave();
    }
    partial void OnIsAnimationEnabledChanged(bool value)
    {
        _uiRuntime.SetAnimationsEnabled(value);

        if (_suppressSave)
        {
            return;
        }

        ScheduleSave();
    }
    partial void OnBackdropChanged(string value)
    {
        var option = ResolveSettingsOption(BackdropOptions, value, "System");
        if (!ReferenceEquals(SelectedBackdropOption, option))
        {
            SelectedBackdropOption = option;
        }

        ScheduleSave();
    }
    partial void OnLaunchOnStartupChanged(bool value)
    {
        if (_suppressSave)
        {
            return;
        }

        ScheduleSave();
        _ = ApplyLaunchOnStartupAsync(value);
    }
    partial void OnMinimizeToTrayChanged(bool value) => ScheduleSave();
    partial void OnLanguageChanged(string? oldValue, string newValue)
    {
        var normalized = AppLanguageCatalog.NormalizeTag(newValue);
        if (!string.Equals(newValue, normalized, StringComparison.Ordinal))
        {
            Language = normalized;
            return;
        }

        var option = ResolveLanguageOption(normalized);
        if (!ReferenceEquals(SelectedLanguageOption, option))
        {
            SelectedLanguageOption = option;
        }

        if (_suppressSave)
        {
            return;
        }

        ScheduleSave();
        if (string.Equals(normalized, _appliedLanguage, StringComparison.Ordinal))
        {
            return;
        }

        _ = ApplyLanguageChangeAsync(_appliedLanguage, normalized);
    }
    partial void OnSelectedLanguageOptionChanged(AppLanguageOptionViewModel? value)
    {
        if (value is null)
        {
            return;
        }

        var normalized = AppLanguageCatalog.NormalizeTag(value.Tag);
        if (!string.Equals(Language, normalized, StringComparison.Ordinal))
        {
            Language = normalized;
        }
    }
    partial void OnSelectedThemeOptionChanged(SettingsOptionViewModel? value)
    {
        if (value is not null && !string.Equals(Theme, value.Value, StringComparison.Ordinal))
        {
            Theme = value.Value;
        }
    }
    partial void OnSelectedBackdropOptionChanged(SettingsOptionViewModel? value)
    {
        if (value is not null && !string.Equals(Backdrop, value.Value, StringComparison.Ordinal))
        {
            Backdrop = value.Value;
        }
    }
    partial void OnLastSelectedServerIdChanged(string? value) => ScheduleSave();
    partial void OnSaveLocalHistoryChanged(bool value) => ScheduleSave();
    partial void OnCacheRetentionDaysChanged(int value) => ScheduleSave();
    partial void OnTelemetrySharingEnabledChanged(bool value) => ScheduleSave();
    partial void OnTelemetryCustomEndpointChanged(string? value) => ScheduleSave();
    partial void OnTelemetryAuthHeaderChanged(string? value) => ScheduleSave();
    partial void OnCloudConfigSyncChanged(CloudConfigSyncSettings value) => ScheduleSave();
    partial void OnKeyboardShortcutsEnabledChanged(bool value)
    {
        NotifyShortcutConfigurationChanged();
        ScheduleSave();
    }
    partial void OnLastSelectedProjectIdChanged(string? value) => ScheduleSave();
    partial void OnAcpEnableConnectionEvictionChanged(bool value) => ScheduleSave();
    partial void OnAcpConnectionIdleTtlMinutesChanged(int? value) => ScheduleSave();
    partial void OnAcpMaxWarmProfilesChanged(int? value) => ScheduleSave();
    partial void OnAcpMaxPinnedProfilesChanged(int? value) => ScheduleSave();
    partial void OnAcpHydrationCompletionModeChanged(string value) => ScheduleSave();

    private void OnProjectsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ScheduleSave();
    }

    private void OnAgentRemoteDirectoriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // A removed remote directory must not leave a dangling navigation membership id behind.
        // Membership is reference-only (see NavigationRemoteDirectoryIds); prune ids whose
        // authoritative directory no longer exists so stale nav nodes cannot be resolved.
        PruneOrphanedNavigationRemoteDirectoryIds();
        ScheduleSave();
    }

    private void OnNavigationRemoteDirectoryIdsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ScheduleSave();
    }

    private void PruneOrphanedNavigationRemoteDirectoryIds()
    {
        if (NavigationRemoteDirectoryIds.Count == 0)
        {
            return;
        }

        var knownDirectoryIds = new HashSet<string>(
            AgentRemoteDirectories
                .Select(directory => directory.DirectoryId?.Trim())
                .Where(id => !string.IsNullOrWhiteSpace(id))!,
            StringComparer.Ordinal);

        for (var i = NavigationRemoteDirectoryIds.Count - 1; i >= 0; i--)
        {
            var membershipId = NavigationRemoteDirectoryIds[i]?.Trim();
            if (string.IsNullOrWhiteSpace(membershipId) || !knownDirectoryIds.Contains(membershipId))
            {
                NavigationRemoteDirectoryIds.RemoveAt(i);
            }
        }
    }

    private void OnKeyBindingsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            foreach (var item in e.NewItems.OfType<KeyBindingPairViewModel>())
            {
                item.PropertyChanged += OnKeyBindingPairPropertyChanged;
            }
        }

        if (e.OldItems != null)
        {
            foreach (var item in e.OldItems.OfType<KeyBindingPairViewModel>())
            {
                item.PropertyChanged -= OnKeyBindingPairPropertyChanged;
            }
        }

        NotifyShortcutConfigurationChanged();
        ScheduleSave();
    }

    private void OnKeyBindingPairPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(KeyBindingPairViewModel.Gesture) ||
            e.PropertyName == nameof(KeyBindingPairViewModel.ActionId))
        {
            NotifyShortcutConfigurationChanged();
            ScheduleSave();
        }
    }

    private void NotifyShortcutConfigurationChanged()
    {
        ShortcutConfigurationChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task ApplyLanguageChangeAsync(string previousLanguage, string normalized)
    {
        try
        {
            await _languageService.ApplyLanguageOverrideAsync(normalized).ConfigureAwait(false);
            _appliedLanguage = normalized;
            _uiRuntime.ReloadShell();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply language override");
            await RevertLanguageAsync(previousLanguage).ConfigureAwait(false);
            await _ui.ShowInfoAsync(_localizer["General_LanguageApplyFailed"]).ConfigureAwait(false);
        }
    }

    private Task RevertLanguageAsync(string previousLanguage)
    {
        var normalizedPrevious = AppLanguageCatalog.NormalizeTag(previousLanguage);
        return _uiDispatcher.EnqueueAsync(() =>
        {
            if (string.Equals(Language, normalizedPrevious, StringComparison.Ordinal))
            {
                return;
            }

            // Suppress apply/save re-entry while restoring the last successful language.
            _suppressSave = true;
            try
            {
                Language = normalizedPrevious;
            }
            finally
            {
                _suppressSave = false;
            }

            ScheduleSave();
        });
    }

    public void SetKeyBinding(string actionId, string gesture)
    {
        if (string.IsNullOrWhiteSpace(actionId))
        {
            return;
        }

        var existing = KeyBindings.FirstOrDefault(k => string.Equals(k.ActionId, actionId, StringComparison.OrdinalIgnoreCase));
        if (existing == null)
        {
            KeyBindings.Add(new KeyBindingPairViewModel(actionId.Trim(), gesture.Trim()));
            return;
        }

        existing.Gesture = gesture.Trim();
    }

    public string? GetKeyBinding(string actionId)
    {
        return KeyBindings.FirstOrDefault(k => string.Equals(k.ActionId, actionId, StringComparison.OrdinalIgnoreCase))?.Gesture;
    }

    public void RemoveKeyBinding(string actionId)
    {
        var existing = KeyBindings.FirstOrDefault(k => string.Equals(k.ActionId, actionId, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            KeyBindings.Remove(existing);
        }
    }

    public void ClearShortcutOverrides()
    {
        KeyBindings.Clear();
    }

    public void ResetToDefaults()
    {
        _suppressSave = true;
        try
        {
            Theme = "System";
            IsAnimationEnabled = true;
            Backdrop = "System";
            LaunchOnStartup = false;
            MinimizeToTray = true;
            Language = "System";
            _appliedLanguage = "System";
            LastSelectedServerId = null;
            SaveLocalHistory = true;
            CacheRetentionDays = 7;
            TelemetrySharingEnabled = true;
            TelemetryCustomEndpoint = null;
            TelemetryAuthHeader = null;
            CloudConfigSync = new CloudConfigSyncSettings();
            KeyboardShortcutsEnabled = true;
            AcpEnableConnectionEviction = false;
            AcpConnectionIdleTtlMinutes = null;
            AcpMaxWarmProfiles = null;
            AcpMaxPinnedProfiles = null;
            AcpHydrationCompletionMode = "StrictReplay";
            KeyBindings.Clear();
        }
        finally
        {
            _suppressSave = false;
        }

        ScheduleSave();
    }

    private void ScheduleSave()
    {
        if (_suppressSave)
        {
            return;
        }

        CancelPendingSave();
        _saveCts = new CancellationTokenSource();
        var token = _saveCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(750), token).ConfigureAwait(false);
                AppSettings? snapshot = null;
                await _uiDispatcher.EnqueueAsync(() => snapshot = CreateSettingsSnapshot()).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();

                if (snapshot is not null)
                {
                    await _appSettingsService.SaveAsync(snapshot).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save app settings");
                try
                {
                    await _ui.ShowInfoAsync(_localizer["General_AppSettingsSaveFailed"]).ConfigureAwait(false);
                }
                catch (Exception showEx)
                {
                    _logger.LogWarning(showEx, "Failed to surface app settings save failure");
                }
            }
        }, token);
    }

    private void CancelPendingSave()
    {
        _saveCts?.Cancel();
        _saveCts?.Dispose();
        _saveCts = null;
    }

    private AppSettings CreateSettingsSnapshot()
        => new()
        {
            Theme = Theme,
            IsAnimationEnabled = IsAnimationEnabled,
            Backdrop = Backdrop,
            LaunchOnStartup = LaunchOnStartup,
            MinimizeToTray = MinimizeToTray,
            Language = AppLanguageCatalog.NormalizeTag(Language),
            LastSelectedServerId = LastSelectedServerId,
            SaveLocalHistory = SaveLocalHistory,
            CacheRetentionDays = CacheRetentionDays,
            TelemetrySharingEnabled = TelemetrySharingEnabled,
            // 空白输入归一化为 null，使 TelemetrySettings.Build 正确回退到部署环境变量；
            // 应用自身不配置默认 collector。
            // 反向验证记录：移除这三行会使 ScheduleSave_PersistsTelemetryToggle 与
            // ScheduleSave_TrimsTelemetryEndpointWhitespace 失败。
            TelemetryCustomEndpoint = NormalizeOptional(TelemetryCustomEndpoint),
            TelemetryAuthHeader = NormalizeOptional(TelemetryAuthHeader),
            CloudConfigSync = CloneCloudConfigSyncSettings(CloudConfigSync),
            KeyboardShortcutsEnabled = KeyboardShortcutsEnabled,
            AcpEnableConnectionEviction = AcpEnableConnectionEviction,
            AcpConnectionIdleTtlMinutes = AcpConnectionIdleTtlMinutes,
            AcpMaxWarmProfiles = AcpMaxWarmProfiles,
            AcpMaxPinnedProfiles = AcpMaxPinnedProfiles,
            AcpHydrationCompletionMode = string.IsNullOrWhiteSpace(AcpHydrationCompletionMode)
                ? "StrictReplay"
                : AcpHydrationCompletionMode.Trim(),
            AgentRemoteDirectories = NormalizeAgentRemoteDirectories(AgentRemoteDirectories),
            NavigationRemoteDirectoryIds = NormalizeNavigationRemoteDirectoryIds(
                NavigationRemoteDirectoryIds,
                AgentRemoteDirectories),
            LastSelectedProjectId = LastSelectedProjectId,
            Projects = Projects
                .Where(p => !string.IsNullOrWhiteSpace(p.ProjectId)
                            && !string.IsNullOrWhiteSpace(p.Name)
                            && !string.IsNullOrWhiteSpace(p.RootPath))
                .Select(p => new ProjectDefinition
                {
                    ProjectId = p.ProjectId.Trim(),
                    Name = p.Name.Trim(),
                    RootPath = p.RootPath.Trim()
                })
                .ToList(),
            KeyBindings = KeyBindings
                .Where(k => !string.IsNullOrWhiteSpace(k.ActionId) && !string.IsNullOrWhiteSpace(k.Gesture))
                .GroupBy(k => k.ActionId.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Last().Gesture.Trim(), StringComparer.OrdinalIgnoreCase)
        };

    public void SetCloudConfigSyncSettings(CloudConfigSyncSettings settings)
    {
        CloudConfigSync = CloneCloudConfigSyncSettings(settings);
    }

    /// <summary>
    /// 把空白输入归一化为 null。
    ///
    /// 必要性：<c>TelemetrySettings.Build</c> 用空值判定用户是否自定义了端点；
    /// 空字符串必须视作未配置，才能回退到部署环境变量。
    /// 用户清空输入框后必须落成 null，才能正确回退到部署环境变量。
    /// </summary>
    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async Task ApplyLaunchOnStartupAsync(bool enabled)
    {
        if (!_startupService.IsSupported)
        {
            return;
        }

        try
        {
            var ok = await _startupService.SetLaunchOnStartupAsync(enabled).ConfigureAwait(false);
            if (!ok)
            {
                _logger.LogWarning("LaunchOnStartup request failed or was denied.");
                await RevertLaunchOnStartupAsync(enabled).ConfigureAwait(false);
                await _ui.ShowInfoAsync(_localizer["General_LaunchOnStartupFailed"]).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to apply launch-on-startup setting");
            await RevertLaunchOnStartupAsync(enabled).ConfigureAwait(false);
            await _ui.ShowInfoAsync(_localizer["General_LaunchOnStartupFailed"]).ConfigureAwait(false);
        }
    }

    private Task RevertLaunchOnStartupAsync(bool attemptedEnabled)
    {
        return _uiDispatcher.EnqueueAsync(() =>
        {
            if (LaunchOnStartup == !attemptedEnabled)
            {
                return;
            }

            // Suppress ScheduleSave/Apply while restoring authoritative OS state.
            _suppressSave = true;
            try
            {
                LaunchOnStartup = !attemptedEnabled;
            }
            finally
            {
                _suppressSave = false;
            }
        });
    }

    private static CloudConfigSyncSettings CloneCloudConfigSyncSettings(CloudConfigSyncSettings? settings)
    {
        if (settings is null)
        {
            return new CloudConfigSyncSettings();
        }

        return new CloudConfigSyncSettings
        {
            Enabled = settings.Enabled,
            ProviderId = settings.ProviderId?.Trim() ?? string.Empty,
            IncludeSecrets = settings.IncludeSecrets,
            ProviderOptions = CloneProviderOptions(settings.ProviderOptions)
        };
    }

    private static Dictionary<string, Dictionary<string, string>> CloneProviderOptions(
        IReadOnlyDictionary<string, Dictionary<string, string>>? options)
    {
        var clone = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        if (options is null)
        {
            return clone;
        }

        foreach (var provider in options)
        {
            if (string.IsNullOrWhiteSpace(provider.Key) || provider.Value is null)
            {
                continue;
            }

            clone[provider.Key.Trim()] = provider.Value
                .Where(option => !string.IsNullOrWhiteSpace(option.Key) && option.Value is not null)
                .ToDictionary(
                    option => option.Key.Trim(),
                    option => option.Value.Trim(),
                    StringComparer.OrdinalIgnoreCase);
        }

        return clone;
    }

    private static List<string> NormalizeNavigationRemoteDirectoryIds(
        IEnumerable<string>? ids,
        IEnumerable<AgentRemoteDirectory>? directories)
    {
        var normalized = new List<string>();
        if (ids is null)
        {
            return normalized;
        }

        // Only keep membership entries whose directory still exists in the authoritative
        // AgentRemoteDirectories collection. The list stores IDs only — never paths — so
        // renaming a remote directory or changing its path never desynchronizes membership.
        var knownIds = new HashSet<string>(StringComparer.Ordinal);
        if (directories is not null)
        {
            foreach (var directory in directories)
            {
                if (directory is null)
                {
                    continue;
                }

                var directoryId = directory.DirectoryId?.Trim();
                if (!string.IsNullOrWhiteSpace(directoryId))
                {
                    knownIds.Add(directoryId);
                }
            }
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            var trimmed = id?.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || !knownIds.Contains(trimmed))
            {
                continue;
            }

            if (seen.Add(trimmed))
            {
                normalized.Add(trimmed);
            }
        }

        return normalized;
    }

    private static List<AgentRemoteDirectory> NormalizeAgentRemoteDirectories(IEnumerable<AgentRemoteDirectory>? directories)
    {
        var normalized = new List<AgentRemoteDirectory>();
        if (directories is null)
        {
            return normalized;
        }

        foreach (var directory in directories)
        {
            if (directory is null)
            {
                continue;
            }

            var directoryId = directory.DirectoryId?.Trim();
            var remotePath = directory.RemotePath?.Trim();
            if (string.IsNullOrWhiteSpace(directoryId)
                || string.IsNullOrWhiteSpace(remotePath)
                || !ProtocolPathRules.IsAbsolutePath(remotePath))
            {
                // Non-absolute paths would only fail later at session/new; drop them here.
                continue;
            }

            // Persist DisplayName trim-only: a blank DisplayName is intentionally preserved so storage
            // stays consistent with the live-edit path. Consumers (Start project selector,
            // project affinity) fall back to RemotePath at display time.
            var normalizedDirectory = new AgentRemoteDirectory
            {
                DirectoryId = directoryId,
                DisplayName = directory.DisplayName?.Trim() ?? string.Empty,
                RemotePath = remotePath
            };

            var duplicateIndex = FindEquivalentRemoteDirectoryIndex(normalized, normalizedDirectory.RemotePath);
            if (duplicateIndex >= 0)
            {
                normalized[duplicateIndex] = normalizedDirectory;
                continue;
            }

            normalized.Add(normalizedDirectory);
        }

        return normalized;
    }

    private static int FindEquivalentRemoteDirectoryIndex(
        IReadOnlyList<AgentRemoteDirectory> directories,
        string remotePath)
    {
        for (var i = 0; i < directories.Count; i++)
        {
            if (PathsEqual(directories[i].RemotePath, remotePath))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool PathsEqual(string? left, string? right)
    {
        var normalizedLeft = NormalizePath(left);
        var normalizedRight = NormalizePath(right);
        var comparison = UsesCaseInsensitivePathSemantics(normalizedLeft) || UsesCaseInsensitivePathSemantics(normalizedRight)
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(normalizedLeft, normalizedRight, comparison);
    }

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return path.Trim().Replace('\\', '/').TrimEnd('/');
    }

    private static bool UsesCaseInsensitivePathSemantics(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        return ProtocolPathRules.IsAbsolutePath(path)
            && (path.StartsWith(@"\\", StringComparison.Ordinal)
                || (path.Length >= 3
                    && char.IsLetter(path[0])
                    && path[1] == ':'
                    && path[2] == '/'));
    }

    private static ObservableCollection<AppLanguageOptionViewModel> CreateLanguageOptions()
        => new(AppLanguageCatalog.SupportedOptions.Select(option =>
            new AppLanguageOptionViewModel(
                option.Tag,
                option.DisplayNameResourceKey)));

    private static ObservableCollection<SettingsOptionViewModel> CreateThemeOptions() =>
        new(
        [
            new SettingsOptionViewModel("System", "Appearance_ThemeSystem.Content"),
            new SettingsOptionViewModel("Light", "Appearance_ThemeLight.Content"),
            new SettingsOptionViewModel("Dark", "Appearance_ThemeDark.Content")
        ]);

    private static ObservableCollection<SettingsOptionViewModel> CreateBackdropOptions() =>
        new(
        [
            new SettingsOptionViewModel("System", "Appearance_BackdropSystem.Content"),
            new SettingsOptionViewModel("Mica", "Appearance_BackdropMica.Content"),
            new SettingsOptionViewModel("Acrylic", "Appearance_BackdropAcrylic.Content"),
            new SettingsOptionViewModel("Solid", "Appearance_BackdropSolid.Content")
        ]);

    private AppLanguageOptionViewModel ResolveLanguageOption(string? tag)
    {
        var normalized = AppLanguageCatalog.NormalizeTag(tag);
        return LanguageOptions.FirstOrDefault(option =>
                   string.Equals(option.Tag, normalized, StringComparison.Ordinal))
               ?? LanguageOptions.First(option =>
                   string.Equals(option.Tag, AppLanguageCatalog.SystemTag, StringComparison.Ordinal));
    }

    private static SettingsOptionViewModel ResolveSettingsOption(
        IEnumerable<SettingsOptionViewModel> options,
        string? value,
        string fallbackValue)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? fallbackValue : value.Trim();
        return options.FirstOrDefault(option =>
                   string.Equals(option.Value, normalized, StringComparison.Ordinal))
               ?? options.First(option =>
                   string.Equals(option.Value, fallbackValue, StringComparison.Ordinal));
    }
}
