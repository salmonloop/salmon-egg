using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Localization;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Resources;
using SalmonEgg.Presentation.Core.Services.Shortcuts;

namespace SalmonEgg.Presentation.ViewModels.Settings;

public sealed partial class ShortcutsSettingsViewModel : ObservableObject, IDisposable
{
    private readonly AppPreferencesViewModel _preferences;
    private readonly IStringLocalizer<CoreStrings> _localizer;
    private readonly IAppLanguageService? _languageService;
    private bool _isApplyingPreferenceState;

    public ObservableCollection<ShortcutEntryViewModel> Shortcuts { get; } = new();
    public AppPreferencesViewModel Preferences => _preferences;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasConflicts))]
    [NotifyPropertyChangedFor(nameof(ConflictMessage))]
    private bool _hasInvalid;

    public bool HasConflicts => Shortcuts
        .Where(s => !string.IsNullOrWhiteSpace(s.Gesture))
        .GroupBy(s => s.Gesture.Trim(), StringComparer.OrdinalIgnoreCase)
        .Any(g => g.Count() > 1);

    public string ConflictMessage
    {
        get
        {
            if (HasInvalid)
            {
                return _localizer["Shortcuts_InvalidGestureMessage"];
            }

            if (!HasConflicts)
            {
                return string.Empty;
            }

            var conflicts = Shortcuts
                .Where(s => !string.IsNullOrWhiteSpace(s.Gesture))
                .GroupBy(s => s.Gesture.Trim(), StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .Take(3)
                .ToArray();

            return _localizer["Shortcuts_ConflictMessage", string.Join(_localizer["Shortcuts_ConflictSeparator"], conflicts)];
        }
    }

    public ShortcutsSettingsViewModel(
        AppPreferencesViewModel preferences,
        IStringLocalizer<CoreStrings> localizer,
        IAppLanguageService? languageService = null)
    {
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _languageService = languageService;

        PruneUnsupportedBindings();
        SeedDefaults();
        ApplyShortcutStateFromPreferences();

        Shortcuts.CollectionChanged += OnShortcutsCollectionChanged;
        foreach (var s in Shortcuts)
        {
            s.PropertyChanged += OnShortcutPropertyChanged;
        }

        if (_languageService is not null)
        {
            _languageService.LanguageChanged += OnLanguageChanged;
        }
    }

    public void Dispose()
    {
        if (_languageService is not null)
        {
            _languageService.LanguageChanged -= OnLanguageChanged;
        }

        Shortcuts.CollectionChanged -= OnShortcutsCollectionChanged;
        foreach (var shortcut in Shortcuts)
        {
            shortcut.PropertyChanged -= OnShortcutPropertyChanged;
        }
    }

    private void SeedDefaults()
    {
        if (Shortcuts.Count > 0)
        {
            return;
        }

        foreach (var definition in AppShortcutCatalog.EditableActions)
        {
            Shortcuts.Add(new ShortcutEntryViewModel(
                definition.ActionId,
                ResolveActionDisplayName(definition),
                definition.DefaultGesture));
        }
    }

    private void PruneUnsupportedBindings()
    {
        var unsupportedActionIds = _preferences.KeyBindings
            .Where(binding => !AppShortcutCatalog.TryGet(binding.ActionId, out _))
            .Select(binding => binding.ActionId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var actionId in unsupportedActionIds)
        {
            _preferences.RemoveKeyBinding(actionId);
        }
    }

    private void ApplyShortcutStateFromPreferences()
    {
        _isApplyingPreferenceState = true;
        try
        {
            foreach (var shortcut in Shortcuts)
            {
                var saved = _preferences.GetKeyBinding(shortcut.ActionId);
                shortcut.Gesture = string.IsNullOrWhiteSpace(saved)
                    ? shortcut.DefaultGesture
                    : saved;
            }

            HasInvalid = Shortcuts.Any(shortcut => !shortcut.IsGestureValid);
            Recompute();
        }
        finally
        {
            _isApplyingPreferenceState = false;
        }
    }

    private void OnShortcutsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            foreach (var item in e.NewItems.OfType<ShortcutEntryViewModel>())
            {
                item.PropertyChanged += OnShortcutPropertyChanged;
            }
        }

        if (e.OldItems != null)
        {
            foreach (var item in e.OldItems.OfType<ShortcutEntryViewModel>())
            {
                item.PropertyChanged -= OnShortcutPropertyChanged;
            }
        }

        Recompute();
    }

    private void OnShortcutPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShortcutEntryViewModel.Gesture))
        {
            if (_isApplyingPreferenceState)
            {
                HasInvalid = Shortcuts.Any(shortcut => !shortcut.IsGestureValid);
                Recompute();
                return;
            }

            var shortcut = (ShortcutEntryViewModel)sender!;
            if (!shortcut.IsGestureValid)
            {
                HasInvalid = true;
                Recompute();
                return;
            }

            shortcut.NormalizeGesture();
            HasInvalid = Shortcuts.Any(s => !s.IsGestureValid);
            if (string.IsNullOrWhiteSpace(shortcut.Gesture))
            {
                _preferences.RemoveKeyBinding(shortcut.ActionId);
            }
            else
            {
                _preferences.SetKeyBinding(shortcut.ActionId, shortcut.Gesture);
            }

            Recompute();
        }
    }

    private void Recompute()
    {
        OnPropertyChanged(nameof(HasConflicts));
        OnPropertyChanged(nameof(ConflictMessage));
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
        => ReprojectLocalizedActionNames();

    private void ReprojectLocalizedActionNames()
    {
        foreach (var shortcut in Shortcuts)
        {
            if (!AppShortcutCatalog.TryGet(shortcut.ActionId, out var definition))
            {
                continue;
            }

            shortcut.UpdateName(ResolveActionDisplayName(definition));
        }

        OnPropertyChanged(nameof(ConflictMessage));
    }

    private string ResolveActionDisplayName(AppShortcutDefinition definition)
    {
        var resourceKey = definition.ActionId switch
        {
            AppShortcutActionIds.NewSession => "ShortcutAction_NewSession",
            AppShortcutActionIds.Search => "ShortcutAction_Search",
            _ => null
        };

        if (resourceKey is null)
        {
            return definition.DisplayName;
        }

        var localized = _localizer[resourceKey];
        return localized.ResourceNotFound || string.IsNullOrWhiteSpace(localized.Value)
            ? definition.DisplayName
            : localized.Value;
    }

    [RelayCommand]
    private void RestoreDefaults()
    {
        _preferences.ClearShortcutOverrides();
        ApplyShortcutStateFromPreferences();
    }
}

public sealed partial class ShortcutEntryViewModel : ObservableObject
{
    public ShortcutEntryViewModel(string actionId, string name, string defaultGesture)
    {
        ActionId = actionId;
        Name = name;
        DefaultGesture = defaultGesture;
        _gesture = defaultGesture;
        RestoreDefaultCommand = new RelayCommand(RestoreDefault);
    }

    public string ActionId { get; }

    public string Name { get; private set; }

    public string DefaultGesture { get; }

    public string RecorderAutomationId => $"Shortcuts.Record.{ActionId}";

    public string RestoreAutomationId => $"Shortcuts.Restore.{ActionId}";

    public IRelayCommand RestoreDefaultCommand { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGestureValid))]
    private string _gesture = string.Empty;

    public bool IsGestureValid
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Gesture))
            {
                return true;
            }

            return AppShortcutGesture.TryParse(Gesture, out _);
        }
    }

    public void NormalizeGesture()
    {
        if (!AppShortcutGesture.TryParse(Gesture, out var parsed))
        {
            return;
        }

        var normalized = parsed.ToString();
        if (!string.Equals(Gesture, normalized, StringComparison.Ordinal))
        {
            Gesture = normalized;
        }
    }

    public void UpdateName(string name)
    {
        if (string.Equals(Name, name, StringComparison.Ordinal))
        {
            return;
        }

        Name = name;
        OnPropertyChanged(nameof(Name));
    }

    private void RestoreDefault()
    {
        Gesture = DefaultGesture;
    }
}
