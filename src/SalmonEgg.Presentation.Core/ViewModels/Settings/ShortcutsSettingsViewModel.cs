using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

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

        Shortcuts.Add(new ShortcutEntryViewModel("new_session", "新建会话", "Ctrl+N"));
        Shortcuts.Add(new ShortcutEntryViewModel("search", "搜索", "Ctrl+K"));
        Shortcuts.Add(new ShortcutEntryViewModel("toggle_right_pane", "切换右侧面板", "Ctrl+\\"));
        Shortcuts.Add(new ShortcutEntryViewModel("focus_input", "聚焦输入框", "Ctrl+L"));
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

            // Minimal validation: "Ctrl+X", "Ctrl+Shift+X", etc.
            var parts = Gesture.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 2)
            {
                return false;
            }

            var key = parts.Last();
            var modifiers = parts.Take(parts.Length - 1).ToArray();
            if (modifiers.Length == 0)
            {
                return false;
            }

            foreach (var m in modifiers)
            {
                if (!string.Equals(m, "Ctrl", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(m, "Alt", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(m, "Shift", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(m, "Win", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return key.Length is >= 1 and <= 12;
        }
    }

    private void RestoreDefault()
    {
        Gesture = DefaultGesture;
    }
}

