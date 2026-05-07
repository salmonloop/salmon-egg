using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SalmonEgg.Presentation.Core.Services.Shortcuts;

namespace SalmonEgg.Presentation.ViewModels.Settings;

public sealed partial class ShortcutsSettingsViewModel : ObservableObject
{
    private readonly AppPreferencesViewModel _preferences;

    public ObservableCollection<ShortcutEntryViewModel> Shortcuts { get; } = new();

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
                return "存在无效快捷键格式，请修正后保存。";
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

            return $"存在冲突：{string.Join("，", conflicts)}";
        }
    }

    public ShortcutsSettingsViewModel(AppPreferencesViewModel preferences)
    {
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));

        PruneUnsupportedBindings();
        SeedDefaults();
        ApplySavedOverrides();

        Shortcuts.CollectionChanged += OnShortcutsCollectionChanged;
        foreach (var s in Shortcuts)
        {
            s.PropertyChanged += OnShortcutPropertyChanged;
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
                definition.DisplayName,
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

    private void ApplySavedOverrides()
    {
        foreach (var shortcut in Shortcuts)
        {
            var saved = _preferences.GetKeyBinding(shortcut.ActionId);
            if (!string.IsNullOrWhiteSpace(saved))
            {
                shortcut.Gesture = saved;
            }
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

    [RelayCommand]
    private void RestoreDefaults()
    {
        foreach (var shortcut in Shortcuts)
        {
            shortcut.Gesture = shortcut.DefaultGesture;
        }
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

    public string Name { get; }

    public string DefaultGesture { get; }

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

    private void RestoreDefault()
    {
        Gesture = DefaultGesture;
    }
}

