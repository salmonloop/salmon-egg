using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.ProjectAffinity;
using SalmonEgg.Domain.Services;
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
    private readonly ILogger<AppPreferencesViewModel> _logger;
    private readonly IUiDispatcher _uiDispatcher;
    private CancellationTokenSource? _saveCts;
    private bool _suppressSave;

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
    private int _historyRetentionDays = 30;

    [ObservableProperty]
    private bool _rememberRecentProjectPaths = true;

    [ObservableProperty]
    private int _cacheRetentionDays = 7;

    // Navigation state (Projects -> Sessions) lives in AppSettings to persist between launches.
    // Keep a single writer (AppPreferencesViewModel) to avoid settings overwrite races.
    public ObservableCollection<ProjectDefinition> Projects { get; } = new();

    public ObservableCollection<ProjectPathMapping> ProjectPathMappings { get; } = new();

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

    public ObservableCollection<KeyBindingPairViewModel> KeyBindings { get; } = new();

    public bool IsLaunchOnStartupSupported => _capabilities.SupportsLaunchOnStartup;

    public bool IsMinimizeToTraySupported => _capabilities.SupportsTray;

    public bool IsLanguageOverrideSupported => _capabilities.SupportsLanguageOverride;

    public bool IsMiniWindowSupported => _capabilities.SupportsMiniWindow;

    public AppPreferencesViewModel(
        IAppSettingsService appSettingsService,
        IAppStartupService startupService,
        IAppLanguageService languageService,
        IPlatformCapabilityService capabilities,
        IUiRuntimeService uiRuntime,
        ILogger<AppPreferencesViewModel> logger,
        IUiDispatcher uiDispatcher)
    {
        _appSettingsService = appSettingsService ?? throw new ArgumentNullException(nameof(appSettingsService));
        _startupService = startupService ?? throw new ArgumentNullException(nameof(startupService));
        _languageService = languageService ?? throw new ArgumentNullException(nameof(languageService));
        _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        _uiRuntime = uiRuntime ?? throw new ArgumentNullException(nameof(uiRuntime));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
        KeyBindings.CollectionChanged += OnKeyBindingsChanged;
        Projects.CollectionChanged += OnProjectsChanged;
        ProjectPathMappings.CollectionChanged += OnProjectPathMappingsChanged;
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            _suppressSave = true;
            var settings = await _appSettingsService.LoadAsync();
            var launchOnStartup = settings.LaunchOnStartup;
            var shouldPersistAnimation = !settings.IsAnimationEnabled;

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

            _uiDispatcher.Enqueue(() =>
            {
                Theme = settings.Theme;
                // Default to enabled and mark "disable" as not yet supported in UI.
                IsAnimationEnabled = true;
                _uiRuntime.SetAnimationsEnabled(true);
                Backdrop = settings.Backdrop;
                LaunchOnStartup = launchOnStartup;
                MinimizeToTray = settings.MinimizeToTray;
                Language = settings.Language;
                LastSelectedServerId = settings.LastSelectedServerId;
                SaveLocalHistory = settings.SaveLocalHistory;
                HistoryRetentionDays = settings.HistoryRetentionDays;
                RememberRecentProjectPaths = settings.RememberRecentProjectPaths;
                CacheRetentionDays = settings.CacheRetentionDays;
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

                ProjectPathMappings.Clear();
                foreach (var mapping in NormalizeProjectPathMappings(settings.ProjectPathMappings))
                {
                    ProjectPathMappings.Add(mapping);
                }

                KeyBindings.Clear();
                foreach (var kvp in settings.KeyBindings)
                {
                    var pair = new KeyBindingPairViewModel(kvp.Key, kvp.Value);
                    pair.PropertyChanged += OnKeyBindingPairPropertyChanged;
                    KeyBindings.Add(pair);
                }

                if (shouldPersistAnimation)
                {
                    _suppressSave = false;
                    ScheduleSave();
                }
            });

            _ = _languageService.ApplyLanguageOverrideAsync(settings.Language);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load app settings");
        }
        finally
        {
            _uiDispatcher.Enqueue(() =>
            {
                IsLoaded = true;
                _suppressSave = false;
            });
        }
    }

    partial void OnThemeChanged(string value) => ScheduleSave();
    partial void OnIsAnimationEnabledChanged(bool value)
    {
        _uiRuntime.SetAnimationsEnabled(value);

        if (_suppressSave)
        {
            return;
        }

        ScheduleSave();
    }
    partial void OnBackdropChanged(string value) => ScheduleSave();
    partial void OnLaunchOnStartupChanged(bool value)
    {
        ScheduleSave();
        _ = ApplyLaunchOnStartupAsync(value);
    }
    partial void OnMinimizeToTrayChanged(bool value) => ScheduleSave();
    partial void OnLanguageChanged(string value)
    {
        if (_suppressSave)
        {
            return;
        }

        ScheduleSave();
        _ = _languageService.ApplyLanguageOverrideAsync(value);
        _uiRuntime.ReloadShell();
    }
    partial void OnLastSelectedServerIdChanged(string? value) => ScheduleSave();
    partial void OnSaveLocalHistoryChanged(bool value) => ScheduleSave();
    partial void OnHistoryRetentionDaysChanged(int value) => ScheduleSave();
    partial void OnRememberRecentProjectPathsChanged(bool value) => ScheduleSave();
    partial void OnCacheRetentionDaysChanged(int value) => ScheduleSave();
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

    private void OnProjectPathMappingsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ScheduleSave();
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

        ScheduleSave();
    }

    private void OnKeyBindingPairPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(KeyBindingPairViewModel.Gesture) ||
            e.PropertyName == nameof(KeyBindingPairViewModel.ActionId))
        {
            ScheduleSave();
        }
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
            LastSelectedServerId = null;
            SaveLocalHistory = true;
            HistoryRetentionDays = 30;
            RememberRecentProjectPaths = true;
            CacheRetentionDays = 7;
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

        var cts = new CancellationTokenSource();
        var token = cts.Token;

        var oldCts = Interlocked.Exchange(ref _saveCts, cts);
        if (oldCts != null)
        {
            oldCts.Cancel();
            oldCts.Dispose();
        }

        // Snapshot UI-bound collections synchronously on the calling thread
        // to prevent "Collection was modified" exceptions when enumerated in the background task.
        List<ProjectPathMapping> projectPathMappingsSnapshot;
        List<ProjectDefinition> projectsSnapshot;
        Dictionary<string, string> keyBindingsSnapshot;

        try
        {
            projectPathMappingsSnapshot = NormalizeProjectPathMappings(ProjectPathMappings);

            projectsSnapshot = Projects
                .Where(p => !string.IsNullOrWhiteSpace(p.ProjectId)
                            && !string.IsNullOrWhiteSpace(p.Name)
                            && !string.IsNullOrWhiteSpace(p.RootPath))
                .Select(p => new ProjectDefinition
                {
                    ProjectId = p.ProjectId.Trim(),
                    Name = p.Name.Trim(),
                    RootPath = p.RootPath.Trim()
                })
                .ToList();

            keyBindingsSnapshot = KeyBindings
                .Where(k => !string.IsNullOrWhiteSpace(k.ActionId) && !string.IsNullOrWhiteSpace(k.Gesture))
                .GroupBy(k => k.ActionId.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Last().Gesture.Trim(), StringComparer.OrdinalIgnoreCase);
        }
        catch (InvalidOperationException)
        {
            // If the collection was modified during synchronous snapshotting (e.g. from tests
            // that simulate concurrent property assignments on different threads), we just skip
            // this save cycle. A subsequent valid change will trigger another schedule anyway.
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(750), token).ConfigureAwait(false);
                await _appSettingsService.SaveAsync(new AppSettings
                {
                    Theme = Theme,
                    IsAnimationEnabled = IsAnimationEnabled,
                    Backdrop = Backdrop,
                    LaunchOnStartup = LaunchOnStartup,
                    MinimizeToTray = MinimizeToTray,
                    Language = Language,
                    LastSelectedServerId = LastSelectedServerId,
                    SaveLocalHistory = SaveLocalHistory,
                    HistoryRetentionDays = HistoryRetentionDays,
                    RememberRecentProjectPaths = RememberRecentProjectPaths,
                    CacheRetentionDays = CacheRetentionDays,
                    AcpEnableConnectionEviction = AcpEnableConnectionEviction,
                    AcpConnectionIdleTtlMinutes = AcpConnectionIdleTtlMinutes,
                    AcpMaxWarmProfiles = AcpMaxWarmProfiles,
                    AcpMaxPinnedProfiles = AcpMaxPinnedProfiles,
                    AcpHydrationCompletionMode = string.IsNullOrWhiteSpace(AcpHydrationCompletionMode)
                        ? "StrictReplay"
                        : AcpHydrationCompletionMode.Trim(),
                    ProjectPathMappings = projectPathMappingsSnapshot,
                    LastSelectedProjectId = LastSelectedProjectId,
                    Projects = projectsSnapshot,
                    KeyBindings = keyBindingsSnapshot
                }).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save app settings");
            }
        }, token);
    }

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
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to apply launch-on-startup setting");
        }
    }

    private static List<ProjectPathMapping> NormalizeProjectPathMappings(IEnumerable<ProjectPathMapping>? mappings)
    {
        var normalized = new List<ProjectPathMapping>();
        if (mappings is null)
        {
            return normalized;
        }

        foreach (var mapping in mappings)
        {
            if (mapping is null)
            {
                continue;
            }

            var profileId = mapping.ProfileId?.Trim();
            var remoteRoot = mapping.RemoteRootPath?.Trim();
            var localRoot = mapping.LocalRootPath?.Trim();

            if (string.IsNullOrWhiteSpace(profileId) ||
                string.IsNullOrWhiteSpace(remoteRoot) ||
                string.IsNullOrWhiteSpace(localRoot))
            {
                continue;
            }

            normalized.Add(new ProjectPathMapping
            {
                ProfileId = profileId,
                RemoteRootPath = remoteRoot,
                LocalRootPath = localRoot
            });
        }

        return normalized;
    }
}
