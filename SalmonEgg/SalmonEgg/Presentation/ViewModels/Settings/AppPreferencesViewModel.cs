using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Presentation.ViewModels.Settings;

public partial class AppPreferencesViewModel : ObservableObject
{
    private readonly IAppSettingsService _appSettingsService;
    private readonly ILogger<AppPreferencesViewModel> _logger;
    private readonly SynchronizationContext _syncContext;
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

    [ObservableProperty]
    private string? _lastSelectedProjectId;

    public ObservableCollection<KeyBindingPairViewModel> KeyBindings { get; } = new();

    public AppPreferencesViewModel(IAppSettingsService appSettingsService, ILogger<AppPreferencesViewModel> logger)
    {
        _appSettingsService = appSettingsService ?? throw new ArgumentNullException(nameof(appSettingsService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _syncContext = SynchronizationContext.Current ?? new SynchronizationContext();
        KeyBindings.CollectionChanged += OnKeyBindingsChanged;
        Projects.CollectionChanged += OnProjectsChanged;
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            _suppressSave = true;
            var settings = await _appSettingsService.LoadAsync();
            _syncContext.Post(_ =>
            {
                Theme = settings.Theme;
                IsAnimationEnabled = settings.IsAnimationEnabled;
                Backdrop = settings.Backdrop;
                LaunchOnStartup = settings.LaunchOnStartup;
                MinimizeToTray = settings.MinimizeToTray;
                Language = settings.Language;
                LastSelectedServerId = settings.LastSelectedServerId;
                SaveLocalHistory = settings.SaveLocalHistory;
                HistoryRetentionDays = settings.HistoryRetentionDays;
                RememberRecentProjectPaths = settings.RememberRecentProjectPaths;
                CacheRetentionDays = settings.CacheRetentionDays;
                LastSelectedProjectId = settings.LastSelectedProjectId;

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

                KeyBindings.Clear();
                foreach (var kvp in settings.KeyBindings)
                {
                    var pair = new KeyBindingPairViewModel(kvp.Key, kvp.Value);
                    pair.PropertyChanged += OnKeyBindingPairPropertyChanged;
                    KeyBindings.Add(pair);
                }
            }, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load app settings");
        }
        finally
        {
            _suppressSave = false;
        }
    }

    partial void OnThemeChanged(string value) => ScheduleSave();
    partial void OnIsAnimationEnabledChanged(bool value) => ScheduleSave();
    partial void OnBackdropChanged(string value) => ScheduleSave();
    partial void OnLaunchOnStartupChanged(bool value) => ScheduleSave();
    partial void OnMinimizeToTrayChanged(bool value) => ScheduleSave();
    partial void OnLanguageChanged(string value) => ScheduleSave();
    partial void OnLastSelectedServerIdChanged(string? value) => ScheduleSave();
    partial void OnSaveLocalHistoryChanged(bool value) => ScheduleSave();
    partial void OnHistoryRetentionDaysChanged(int value) => ScheduleSave();
    partial void OnRememberRecentProjectPathsChanged(bool value) => ScheduleSave();
    partial void OnCacheRetentionDaysChanged(int value) => ScheduleSave();
    partial void OnLastSelectedProjectIdChanged(string? value) => ScheduleSave();

    private void OnProjectsChanged(object? sender, NotifyCollectionChangedEventArgs e)
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

        _saveCts?.Cancel();
        _saveCts?.Dispose();
        _saveCts = new CancellationTokenSource();
        var token = _saveCts.Token;

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
}
