using System;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Localization;
using SalmonEgg.Presentation.Core.Resources;
using SalmonEgg.Presentation.Core.Services;

namespace SalmonEgg.Presentation.ViewModels.Chat;

internal enum ConfigOptionApplyOutcome { Applied, Failed, Stale }

public partial class ConfigOptionViewModel
{
    private Func<ConfigOptionViewModel, Task<ConfigOptionApplyOutcome>>? _apply;
    private Func<bool>? _isCurrent;
    private IStringLocalizer<CoreStrings>? _localizer;
    private IUiDispatcher? _uiDispatcher;
    private Action? _editingSettled;
    private string? _errorKey;
    private bool _projecting;
    private long _editRevision;

    [ObservableProperty]
    private bool _isApplying;

    [ObservableProperty]
    private bool _changedWhileEditing;

    public string ApplyText => Text("SessionConfig_Apply", "Apply");
    public string ResetText => Text("SessionConfig_Reset", "Reset");
    public string OnText => Text("SessionConfig_On", "On");
    public string OffText => Text("SessionConfig_Off", "Off");
    public string ChangedText => Text("SessionConfig_Changed", "The agent updated this setting. Your edit is kept; apply it or reset to the agent's value.");
    public string? ErrorMessage => _errorKey is null ? null : Text(_errorKey, _errorKey switch
    {
        "SessionConfig_Stale" => "The conversation or connection changed. Reopen session settings.",
        _ => "Could not apply this setting. Your edit is kept; try again."
    });
    public bool HasError => _errorKey is not null;
    public bool EditorEnabled => !IsApplying && _apply is not null && _isCurrent?.Invoke() == true;
    public bool HasChanges => IsBoolType ? Value is bool value && BoolValue != value
        : IsSelectType && !string.Equals(SelectedOption?.Value, Value as string, StringComparison.Ordinal);
    public bool CanApply => EditorEnabled && HasChanges && (IsBoolType
        || IsSelectType && SelectedOption is { } selected && Options.Contains(selected));
    public bool CanReset => EditorEnabled && (HasChanges || HasError || ChangedWhileEditing);

    internal void BindEditor(
        Func<ConfigOptionViewModel, Task<ConfigOptionApplyOutcome>> apply,
        Func<bool> isCurrent,
        IStringLocalizer<CoreStrings>? localizer,
        IUiDispatcher uiDispatcher,
        Action editingSettled)
    {
        _apply = apply ?? throw new ArgumentNullException(nameof(apply));
        _isCurrent = isCurrent ?? throw new ArgumentNullException(nameof(isCurrent));
        _localizer = localizer;
        _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
        _editingSettled = editingSettled ?? throw new ArgumentNullException(nameof(editingSettled));
        RefreshEditor();
    }

    internal void RetireEditor()
    {
        _apply = null;
        _isCurrent = null;
        _editingSettled = null;
        RefreshEditor();
    }

    internal void UpdateAuthoritative(ConfigOptionViewModel current)
    {
        var preserveDraft = HasChanges || IsApplying;
        var previousSelection = SelectedOption;
        var selectedValue = previousSelection?.Value;
        var booleanValue = BoolValue;
        var previousValue = Value;
        _projecting = true;
        try
        {
            Name = current.Name;
            Description = current.Description;
            Category = current.Category;
            Value = current.Value;
            if (!Options.SequenceEqual(current.Options, OptionValueEqualityComparer.Instance))
            {
                Options = current.Options;
            }
            SelectedOption = Options.FirstOrDefault(option => option.Value == (preserveDraft ? selectedValue : Value as string))
                ?? (preserveDraft ? previousSelection : null);
            BoolValue = preserveDraft ? booleanValue : Value is true;
            if (preserveDraft && !IsApplying && (!Equals(previousValue, Value)
                || IsSelectType && SelectedOption is { } selected && !Options.Contains(selected))) ChangedWhileEditing = true;
        }
        finally { _projecting = false; }
        RefreshEditor();
    }

    internal void RefreshEditor()
    {
        OnPropertyChanged(nameof(EditorEnabled));
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(CanReset));
        ApplyCommand.NotifyCanExecuteChanged();
        ResetCommand.NotifyCanExecuteChanged();
    }

    internal void ReprojectLocalizedText()
    {
        OnPropertyChanged(nameof(ApplyText));
        OnPropertyChanged(nameof(ResetText));
        OnPropertyChanged(nameof(OnText));
        OnPropertyChanged(nameof(OffText));
        OnPropertyChanged(nameof(ChangedText));
        OnPropertyChanged(nameof(ErrorMessage));
    }

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        if (_uiDispatcher is null) return;
        await _uiDispatcher.EnqueueAsync(ApplyOnUiAsync).ConfigureAwait(false);
    }

    private async Task ApplyOnUiAsync()
    {
        if (!CanApply || _apply is null) return;
        var revision = _editRevision;
        SetError(null);
        IsApplying = true;
        try
        {
            var outcome = await _apply(this).ConfigureAwait(false);
            await _uiDispatcher!.EnqueueAsync(() =>
            {
                if (outcome == ConfigOptionApplyOutcome.Applied)
                {
                    if (_editRevision == revision) ResetDraft();
                }
                else SetError(outcome == ConfigOptionApplyOutcome.Stale ? "SessionConfig_Stale" : "SessionConfig_Failed");
            }).ConfigureAwait(false);
        }
        finally
        {
            await _uiDispatcher!.EnqueueAsync(() =>
            {
                IsApplying = false;
                _editingSettled?.Invoke();
            }).ConfigureAwait(false);
        }
    }

    [RelayCommand(CanExecute = nameof(CanReset))]
    private void Reset()
    {
        if (!CanReset) return;
        ResetDraft();
        _editingSettled?.Invoke();
    }

    private void ResetDraft()
    {
        _projecting = true;
        try
        {
            SelectedOption = Options.FirstOrDefault(option => option.Value == Value as string);
            BoolValue = Value is true;
            ChangedWhileEditing = false;
            SetError(null);
        }
        finally { _projecting = false; }
        RefreshEditor();
    }

    private void SetError(string? key)
    {
        _errorKey = key;
        OnPropertyChanged(nameof(ErrorMessage));
        OnPropertyChanged(nameof(HasError));
        RefreshEditor();
    }

    private string Text(string key, string fallback)
    {
        var localized = _localizer?[key];
        return localized is null || localized.ResourceNotFound ? fallback : localized.Value;
    }

    partial void OnSelectedOptionChanged(OptionValueViewModel? value)
    {
        if (!_projecting) _editRevision++;
        RefreshEditor();
    }

    partial void OnBoolValueChanged(bool value)
    {
        if (!_projecting) _editRevision++;
        RefreshEditor();
    }

    partial void OnIsApplyingChanged(bool value) => RefreshEditor();
    partial void OnChangedWhileEditingChanged(bool value) => RefreshEditor();

    private sealed class OptionValueEqualityComparer : System.Collections.Generic.IEqualityComparer<OptionValueViewModel>
    {
        public static OptionValueEqualityComparer Instance { get; } = new();
        public bool Equals(OptionValueViewModel? left, OptionValueViewModel? right)
            => left?.Value == right?.Value && left?.Name == right?.Name && left?.Description == right?.Description;
        public int GetHashCode(OptionValueViewModel value) => HashCode.Combine(value.Value, value.Name, value.Description);
    }
}
