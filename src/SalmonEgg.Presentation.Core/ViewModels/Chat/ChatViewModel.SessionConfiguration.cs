using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Services.Chat;

namespace SalmonEgg.Presentation.ViewModels.Chat;

public partial class ChatViewModel
{
    private AcpSessionEventSource? _configurationSource;
    private ConversationBindingSlice? _configurationBinding;

    public string SessionSettingsText => Localize("SessionConfig_Title", "Session settings");
    public string SessionSettingsHint => Localize("SessionConfig_Hint", "Settings supplied by the agent apply to this conversation.");

    private void ReconcileSessionConfiguration(IReadOnlyList<ConfigOptionViewModel> projected)
    {
        var state = _chatStore.ReadCommittedState();
        var binding = state?.ResolveBinding(CurrentSessionId);
        var source = _chatService is { } service
            ? ResolveRegisteredOrCurrentEventSource(service)
            : null;
        if (binding != _configurationBinding || source != _configurationSource)
        {
            foreach (var row in ConfigOptions) row.RetireEditor();
            ConfigOptions.Clear();
            _configurationBinding = binding;
            _configurationSource = source;
        }

        var removed = ConfigOptions.Where(row => !projected.Any(option => option.Id == row.Id && option.ValueType == row.ValueType)).ToArray();
        foreach (var row in removed)
        {
            row.RetireEditor();
            ConfigOptions.Remove(row);
        }
        foreach (var projectedRow in projected)
        {
            var row = ConfigOptions.FirstOrDefault(option => option.Id == projectedRow.Id && option.ValueType == projectedRow.ValueType);
            if (row is not null) row.UpdateAuthoritative(projectedRow);
            else
            {
                row = projectedRow;
                ConfigOptions.Add(row);
                if (source is not null && binding is not null)
                {
                    row.BindEditor(option => ApplySessionConfigurationAsync(option, source, binding),
                        () => IsConfigurationCurrent(row, source, binding), _localizer, _uiDispatcher,
                        ConvergeSessionConfigurationOrder);
                }
            }
            row.RefreshEditor();
        }
        ConvergeSessionConfigurationOrder();
    }

    private void ConvergeSessionConfigurationOrder()
    {
        // Moving native containers while a user is editing risks losing their focus/selection.
        // Once all drafts settle, apply the Agent's current priority order from the same store.
        if (ConfigOptions.Any(row => row.IsApplying || row.HasChanges)) return;
        var state = _chatStore.ReadCommittedState();
        var options = state?.ResolveSessionStateSlice(CurrentSessionId)?.ConfigOptions ?? state?.ConfigOptions;
        if (options is null) return;
        var target = 0;
        foreach (var option in options)
        {
            var row = ConfigOptions.FirstOrDefault(row => row.Id == option.Id);
            if (row is null) continue;
            var current = ConfigOptions.IndexOf(row);
            if (current != target) ConfigOptions.Move(current, target);
            target++;
        }
    }

    private bool IsConfigurationCurrent(ConfigOptionViewModel row, AcpSessionEventSource source, ConversationBindingSlice binding)
    {
        var state = _chatStore.ReadCommittedState();
        if (state is null || state.IsHydrating || IsRemoteHydrationPending
            || !IsInteractionBindingCurrent(source, binding)
            || !ReferenceEquals(_chatService, source.Service)
            || !source.Service.IsInitialized
            || state.HydratedConversationId != binding.ConversationId
            || CurrentSessionId != binding.ConversationId
            || !ConfigOptions.Contains(row)) return false;
        var configuration = state.ResolveSessionStateSlice(binding.ConversationId)?.ConfigOptions ?? state.ConfigOptions;
        return configuration?.Any(option => option.Id == row.Id && (option.ValueType ?? "select") == row.ValueType) == true;
    }

    private async Task<ConfigOptionApplyOutcome> ApplySessionConfigurationAsync(
        ConfigOptionViewModel row, AcpSessionEventSource source, ConversationBindingSlice binding)
    {
        if (!IsConfigurationCurrent(row, source, binding) || !IsConfigurationValueAllowed(row)
            || string.IsNullOrWhiteSpace(binding.RemoteSessionId)
            || string.IsNullOrWhiteSpace(binding.ConversationId))
            return ConfigOptionApplyOutcome.Stale;
        var request = row.IsBoolType
            ? new SessionSetConfigOptionParams(binding.RemoteSessionId, row.Id, row.BoolValue)
            : row.IsSelectType && row.SelectedOption is { } selected && row.Options.Contains(selected)
                ? new SessionSetConfigOptionParams(binding.RemoteSessionId, row.Id, selected.Value)
                : null;
        if (request is null) return ConfigOptionApplyOutcome.Failed;
        try
        {
            var response = await source.Service.SetSessionConfigOptionAsync(request).ConfigureAwait(false);
            var outcome = ConfigOptionApplyOutcome.Stale;
            await PostToUiAsync(async () =>
            {
                if (!IsConfigurationCurrent(row, source, binding)) return;
                if (response?.ConfigOptions is null)
                {
                    outcome = ConfigOptionApplyOutcome.Failed;
                    return;
                }
                await ApplySessionConfigOptionResponseAsync(binding.ConversationId, response, binding.RemoteSessionId).ConfigureAwait(true);
                if (!IsConfigurationCurrent(row, source, binding)) return;
                await ApplyCurrentStoreProjectionAsync().ConfigureAwait(true);
                if (!IsConfigurationCurrent(row, source, binding)) return;
                outcome = ConfigOptionApplyOutcome.Applied;
            }).ConfigureAwait(false);
            return outcome;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Session configuration change failed. ConversationId={ConversationId} ConfigId={ConfigId}", binding.ConversationId, row.Id);
            var outcome = ConfigOptionApplyOutcome.Stale;
            await PostToUiAsync(() => outcome = IsConfigurationCurrent(row, source, binding)
                ? ConfigOptionApplyOutcome.Failed : ConfigOptionApplyOutcome.Stale).ConfigureAwait(false);
            return outcome;
        }
    }

    private bool IsConfigurationValueAllowed(ConfigOptionViewModel row)
    {
        var state = _chatStore.ReadCommittedState();
        var options = state?.ResolveSessionStateSlice(CurrentSessionId)?.ConfigOptions ?? state?.ConfigOptions;
        var option = options?.FirstOrDefault(option => option.Id == row.Id);
        return option is not null && (option.ValueType == "boolean" && row.IsBoolType
            || (option.ValueType is null or "select") && row.IsSelectType && row.SelectedOption is { } selected
                && option.Options.Any(value => value.Value == selected.Value));
    }
}
