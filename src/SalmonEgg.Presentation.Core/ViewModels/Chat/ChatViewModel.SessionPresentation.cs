using System;
using Uno.Extensions.Reactive;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Domain.Interfaces;
using SalmonEgg.Domain.Interfaces.Storage;
using SalmonEgg.Domain.Interfaces.Transport;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.Content;
using SalmonEgg.Domain.Models.JsonRpc;
using SalmonEgg.Domain.Models.Mcp;
using SalmonEgg.Domain.Models.Protocol;
using SalmonEgg.Domain.Models.ProjectAffinity;
using SalmonEgg.Domain.Models.Session;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Core.Services.ProjectAffinity;
using SalmonEgg.Presentation.Core.Services.Input;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.ViewModels.Chat.AskUser;
using SalmonEgg.Presentation.ViewModels.Chat.Hydration;
using SalmonEgg.Presentation.Core.ViewModels.Chat.Input;
using SalmonEgg.Presentation.ViewModels.Chat.Interactions;
using SalmonEgg.Presentation.Core.ViewModels.Chat.Overlay;
using SalmonEgg.Presentation.Core.ViewModels.Chat.PlanPanel;
using SalmonEgg.Presentation.Core.ViewModels.Chat.ProjectAffinity;
using SalmonEgg.Presentation.ViewModels.Chat.Activation;
using SalmonEgg.Presentation.ViewModels.Chat.Transcript;
using SalmonEgg.Presentation.ViewModels.Chat.Panels;
using SalmonEgg.Presentation.Models.Navigation;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.ViewModels.Settings;

namespace SalmonEgg.Presentation.ViewModels.Chat;

public partial class ChatViewModel
{
    private void SyncPlanEntries(IReadOnlyList<ConversationPlanEntrySnapshot> planEntries)
        => _planEntriesProjectionCoordinator.Sync(PlanEntries, planEntries);

    private void ApplySelectedProfileFromStore(string? profileId)
    {
        _selectedProfileIdFromStore = profileId;

        var match = string.IsNullOrWhiteSpace(profileId)
            ? null
            : _acpProfiles.Profiles.FirstOrDefault(p => string.Equals(p.Id, profileId, StringComparison.Ordinal));

        if (!ReferenceEquals(SelectedAcpProfile, match))
        {
            _suppressAcpProfileConnect = true;
            _suppressProfileSyncFromStore = true;
            try
            {
                SelectedAcpProfile = match;
                _acpProfiles.SelectedProfile = match;
            }
            finally
            {
                _suppressProfileSyncFromStore = false;
                _suppressAcpProfileConnect = false;
            }
        }
    }

    private void ApplySettingsSelectedProfileFromStore(string? profileId)
    {
        _settingsSelectedProfileId = profileId;

        var match = string.IsNullOrWhiteSpace(profileId)
            ? null
            : _acpProfiles.Profiles.FirstOrDefault(p => string.Equals(p.Id, profileId, StringComparison.Ordinal));

        if (!ReferenceEquals(SelectedAcpProfile, match))
        {
            _suppressAcpProfileConnect = true;
            _suppressProfileSyncFromStore = true;
            try
            {
                SelectedAcpProfile = match;
                _acpProfiles.SelectedProfile = match;
            }
            finally
            {
                _suppressProfileSyncFromStore = false;
                _suppressAcpProfileConnect = false;
            }
        }
    }

    private ServerConfiguration? ResolveLoadedProfileSelection(ServerConfiguration? profile)
    {
        if (profile is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(profile.Id))
        {
            return _acpProfiles.Profiles.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, profile.Id, StringComparison.Ordinal));
        }

        return _acpProfiles.Profiles.FirstOrDefault(candidate => ReferenceEquals(candidate, profile));
    }

    private void ApplySessionStateProjection(
        IReadOnlyList<ConversationModeOptionSnapshot> availableModes,
        string? selectedModeId,
        IReadOnlyList<ConversationConfigOptionSnapshot> configOptions,
        bool showConfigOptionsPanel)
    {
        if (!ModeCollectionMatches(AvailableModes, availableModes))
        {
            AvailableModes.Clear();
            foreach (var mode in availableModes)
            {
                AvailableModes.Add(new SessionModeViewModel
                {
                    ModeId = mode.ModeId,
                    ModeName = mode.ModeName,
                    Description = mode.Description
                });
            }
        }

        if (!ConfigOptionCollectionMatches(ConfigOptions, configOptions))
        {
            ConfigOptions.Clear();
            foreach (var option in configOptions)
            {
                ConfigOptions.Add(MapConfigOption(option));
            }
        }

        ShowConfigOptionsPanel = showConfigOptionsPanel;

        if (ConfigOptions.Count == 0)
        {
            _modeConfigId = null;
        }
        else
        {
            SyncModesFromConfigOptions();
        }

        if (!string.IsNullOrWhiteSpace(selectedModeId) && AvailableModes.Count > 0)
        {
            SetSelectedModeWithoutDispatch(
                AvailableModes.FirstOrDefault(m => m.ModeId == selectedModeId) ?? AvailableModes[0]);
            return;
        }

        SetSelectedModeWithoutDispatch(AvailableModes.FirstOrDefault());
    }

    private static bool ModeCollectionMatches(
        IReadOnlyList<SessionModeViewModel> current,
        IReadOnlyList<ConversationModeOptionSnapshot> projected)
    {
        if (current.Count != projected.Count)
        {
            return false;
        }

        for (var i = 0; i < current.Count; i++)
        {
            if (!string.Equals(current[i].ModeId, projected[i].ModeId, StringComparison.Ordinal)
                || !string.Equals(current[i].ModeName, projected[i].ModeName, StringComparison.Ordinal)
                || !string.Equals(current[i].Description, projected[i].Description, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ConfigOptionCollectionMatches(
        IReadOnlyList<ConfigOptionViewModel> current,
        IReadOnlyList<ConversationConfigOptionSnapshot> projected)
    {
        if (current.Count != projected.Count)
        {
            return false;
        }

        for (var i = 0; i < current.Count; i++)
        {
            var left = current[i];
            var right = projected[i];
            if (!string.Equals(left.Id, right.Id, StringComparison.Ordinal)
                || !string.Equals(left.Name, right.Name, StringComparison.Ordinal)
                || !string.Equals(left.Description, right.Description, StringComparison.Ordinal)
                || !string.Equals(left.Category, right.Category, StringComparison.Ordinal)
                || !string.Equals(left.ValueType, right.ValueType ?? "string", StringComparison.Ordinal)
                || !string.Equals(left.Value?.ToString(), right.SelectedValue ?? string.Empty, StringComparison.Ordinal))
            {
                return false;
            }

            if (left.Options.Count != right.Options.Count)
            {
                return false;
            }

            for (var optionIndex = 0; optionIndex < left.Options.Count; optionIndex++)
            {
                var leftOption = left.Options[optionIndex];
                var rightOption = right.Options[optionIndex];
                if (!string.Equals(leftOption.Value, rightOption.Value, StringComparison.Ordinal)
                    || !string.Equals(leftOption.Name, rightOption.Name, StringComparison.Ordinal)
                    || !string.Equals(leftOption.Description, rightOption.Description, StringComparison.Ordinal))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private void OnAcpProfilesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_selectedProfileIdFromStore))
        {
            return;
        }

        ApplySelectedProfileFromStore(_selectedProfileIdFromStore);
    }

    partial void OnSelectedAcpProfileChanged(ServerConfiguration? value)
    {
        if (!_suppressStoreProfileProjection)
        {
            _ = _chatConnectionStore.Dispatch(new SetSettingsSelectedProfileAction(value?.Id));
        }

        if (_suppressAcpProfileConnect || value == null)
        {
            return;
        }

        QueueSelectedProfileConnection(value);
    }

    partial void OnSelectedModeChanged(SessionModeViewModel? value)
    {
        if (_suppressModeSelectionDispatch || value is null || _disposed)
        {
            return;
        }

        _ = SetModeAsync(value);
    }

    private void RefreshProjectAffinityCorrectionState(string? conversationId = null)
    {
        var presentedState = _projectAffinityCorrectionCoordinator.Present(
            _conversationWorkspace,
            _sessionManager,
            conversationId,
            CurrentSessionId,
            _currentRemoteSessionId,
            SelectedAcpProfile?.Id,
            SelectedProjectAffinityOverrideProjectId,
            _preferences.Projects.ToArray(),
            _preferences.ProjectPathMappings.ToArray());

        ProjectAffinityOverrideOptions = new ObservableCollection<ProjectAffinityOverrideOptionViewModel>(presentedState.Options);
        IsProjectAffinityCorrectionVisible = presentedState.IsVisible;
        HasProjectAffinityOverride = presentedState.HasOverride;
        EffectiveProjectAffinityProjectId = presentedState.EffectiveProjectId;
        EffectiveProjectAffinitySource = presentedState.EffectiveSource;
        ProjectAffinityCorrectionMessage = presentedState.Message;
        SelectedProjectAffinityOverrideProjectId = presentedState.SelectedOverrideProjectId;

        OnPropertyChanged(nameof(CanApplyProjectAffinityOverride));
        OnPropertyChanged(nameof(CanClearProjectAffinityOverride));
    }

    private void ApplyProjectAffinityOverride()
    {
        if (!_sessionHeaderActionCoordinator.TryApplyProjectAffinityOverride(
            _conversationWorkspace,
            CurrentSessionId,
            SelectedProjectAffinityOverrideProjectId))
        {
            return;
        }

        RefreshProjectAffinityCorrectionState(CurrentSessionId);
        ApplyProjectAffinityOverrideCommand.NotifyCanExecuteChanged();
        ClearProjectAffinityOverrideCommand.NotifyCanExecuteChanged();
    }

    private void ClearProjectAffinityOverride()
    {
        if (!_sessionHeaderActionCoordinator.TryClearProjectAffinityOverride(
            _conversationWorkspace,
            CurrentSessionId))
        {
            return;
        }

        SelectedProjectAffinityOverrideProjectId = null;
        RefreshProjectAffinityCorrectionState(CurrentSessionId);
        ApplyProjectAffinityOverrideCommand.NotifyCanExecuteChanged();
        ClearProjectAffinityOverrideCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void BeginEditSessionName()
    {
        if (!_sessionHeaderActionCoordinator.TryBeginEditSessionName(
            IsSessionActive,
            CurrentSessionId,
            CurrentSessionDisplayName,
            out var editingSessionName))
        {
            return;
        }

        EditingSessionName = editingSessionName;
        IsEditingSessionName = true;
    }

    [RelayCommand]
    private void CancelSessionNameEdit()
    {
        IsEditingSessionName = false;
        EditingSessionName = string.Empty;
    }

    [RelayCommand]
    private void CommitSessionNameEdit()
    {
        if (!IsSessionActive || string.IsNullOrWhiteSpace(CurrentSessionId))
        {
            CancelSessionNameEdit();
            return;
        }

        var sessionId = CurrentSessionId;
        var finalName = _sessionHeaderActionCoordinator.CommitSessionName(sessionId, EditingSessionName);

        RenameConversation(sessionId, finalName);

        CancelSessionNameEdit();
    }

    public void RenameConversation(string conversationId, string newDisplayName)
    {
        _conversationCatalogFacade.RenameConversation(conversationId, newDisplayName);

        if (string.Equals(CurrentSessionId, conversationId, StringComparison.Ordinal))
        {
            CurrentSessionDisplayName = ResolveSessionDisplayName(conversationId);
        }
    }

    public IReadOnlyList<ConversationProjectTargetOption> GetConversationProjectTargets()
        => _conversationCatalogFacade.GetConversationProjectTargets();

    public void MoveConversationToProject(string conversationId, string projectId)
    {
        _conversationCatalogFacade.MoveConversationToProject(conversationId, projectId);

        if (string.Equals(CurrentSessionId, conversationId, StringComparison.Ordinal))
        {
            RefreshProjectAffinityCorrectionState(conversationId);
        }
    }
}
