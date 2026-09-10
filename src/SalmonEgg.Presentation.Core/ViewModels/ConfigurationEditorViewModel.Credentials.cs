using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentValidation.Results;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Localization;

namespace SalmonEgg.Presentation.ViewModels;

public partial class ConfigurationEditorViewModel
{
    private CredentialBinding? _credentialBindingDraft;

    public ObservableCollection<CredentialSourceOption> CredentialSourceOptions { get; } =
    [
        new(CredentialSource.Token, localizer["AgentProfileEditor_CredentialToken"]),
        new(CredentialSource.ApiKey, localizer["AgentProfileEditor_CredentialApiKey"])
    ];

    [ObservableProperty]
    private CredentialSourceOption? _selectedCredentialSourceOption;

    [ObservableProperty]
    private string _credentialName = string.Empty;

    [ObservableProperty]
    private string _credentialScheme = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCredentialInputEnabled))]
    private bool _clearCredentialsOnSave;

    public bool IsCredentialInputEnabled => !IsBusy && !ClearCredentialsOnSave;

    public bool CanBindCredential => !IsBusy && (Transport != TransportType.WebSocket
        || _transportSupportPolicy.SupportsWebSocketRequestHeaders);

    public bool CanRemoveCredentialBinding => !IsBusy && _credentialBindingDraft is not null;

    public string CredentialKeepHint => _localizer["AgentProfileEditor_CredentialKeepHint"];
    public string CredentialClearLabel => _localizer["AgentProfileEditor_CredentialClear"];
    public string CredentialBindingTitle => _localizer["AgentProfileEditor_CredentialBindingTitle"];
    public string CredentialSourceLabel => _localizer["AgentProfileEditor_CredentialSource"];
    public string CredentialNameLabel => _localizer[IsStdio
        ? "AgentProfileEditor_CredentialEnvironmentName"
        : "AgentProfileEditor_CredentialHeaderName"];
    public string CredentialSchemeLabel => _localizer["AgentProfileEditor_CredentialScheme"];
    public string CredentialBindLabel => _localizer["AgentProfileEditor_CredentialBind"];
    public string CredentialUnbindLabel => _localizer["AgentProfileEditor_CredentialUnbind"];
    public string CredentialBindingHint => _localizer[Transport == TransportType.WebSocket
        && !_transportSupportPolicy.SupportsWebSocketRequestHeaders
            ? "AgentProfileEditor_CredentialWebSocketUnsupported"
            : "AgentProfileEditor_CredentialBindingHint"];
    public string CredentialBindingSummary => _credentialBindingDraft is { } binding
        ? _localizer["AgentProfileEditor_CredentialBoundFormat",
            CredentialSourceOptions.FirstOrDefault(option => option.Source == binding.Source)?.Name
                ?? _localizer["AgentProfileEditor_CredentialUnsupported"].Value,
            binding.Name]
        : _localizer["AgentProfileEditor_CredentialUnbound"];

    [RelayCommand(CanExecute = nameof(CanBindCredential))]
    private void BindCredential()
    {
        if (!CanBindCredential || SelectedCredentialSourceOption is null)
        {
            return;
        }

        ServerConfiguration candidate;
        try
        {
            candidate = CreateConfigurationCandidate();
        }
        catch (StdioCommandLineParseException parseError)
        {
            SetError(_localizer["AgentProfileEditor_ValidationFailedFormat", parseError.Message]);
            return;
        }
        candidate.CredentialBinding = CredentialBindingPolicy.Create(
            candidate,
            SelectedCredentialSourceOption.Source,
            IsStdio ? CredentialTarget.Environment : CredentialTarget.Header,
            CredentialName,
            IsStdio || string.IsNullOrEmpty(CredentialScheme) ? null : CredentialScheme);
        if (CredentialBindingPolicy.GetValidationError(candidate) is { } error)
        {
            SetError(_localizer["AgentProfileEditor_ValidationFailedFormat", CredentialBindingErrorMessageFormatter.Format(error, _localizer)]);
            return;
        }

        ClearError();
        // Freeze the approved destination now. Save must not reapprove a destination edited later.
        _credentialBindingDraft = candidate.CredentialBinding;
        RefreshCredentialBindingState();
    }

    [RelayCommand(CanExecute = nameof(CanRemoveCredentialBinding))]
    private void RemoveCredentialBinding()
    {
        if (!CanRemoveCredentialBinding)
        {
            return;
        }

        _credentialBindingDraft = null;
        ClearError();
        RefreshCredentialBindingState();
    }

    private void ResetCredentialEdits()
    {
        Token = string.Empty;
        ApiKey = string.Empty;
        ClearCredentialsOnSave = false;
        _credentialBindingDraft = Configuration.CredentialBinding;
        SelectedCredentialSourceOption = CredentialSourceOptions.FirstOrDefault(option =>
            option.Source == (_credentialBindingDraft?.Source ?? CredentialSource.Token));
        CredentialName = _credentialBindingDraft?.Name ?? string.Empty;
        CredentialScheme = _credentialBindingDraft?.Scheme ?? string.Empty;
        RefreshCredentialBindingState();
    }

    private void RefreshCredentialBindingState()
    {
        OnPropertyChanged(nameof(CanBindCredential));
        OnPropertyChanged(nameof(CanRemoveCredentialBinding));
        OnPropertyChanged(nameof(CredentialNameLabel));
        OnPropertyChanged(nameof(CredentialBindingHint));
        OnPropertyChanged(nameof(CredentialBindingSummary));
        BindCredentialCommand.NotifyCanExecuteChanged();
        RemoveCredentialBindingCommand.NotifyCanExecuteChanged();
    }

    private string FormatValidationFailure(ValidationFailure failure)
        => failure.CustomState is CredentialBindingValidationError bindingError
            ? CredentialBindingErrorMessageFormatter.Format(bindingError, _localizer)
            : failure.ErrorMessage;
}

public sealed class CredentialSourceOption(CredentialSource source, string name)
{
    public CredentialSource Source { get; } = source;
    public string Name { get; } = name;
}
