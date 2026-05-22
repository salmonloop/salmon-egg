using System;
using System.Collections.Generic;
using System.Linq;
using SalmonEgg.Domain.Models;

namespace SalmonEgg.Presentation.Core.ViewModels.Chat.Selectors;

public sealed record AgentSelectorPolicyInput(
    string Identity,
    IReadOnlyList<ServerConfiguration> Agents,
    string? SelectedProfileId,
    bool IsConnecting,
    bool HasConnectionError,
    bool IsSelectionResolved);

public sealed class AgentSelectorPolicy
{
    public SelectorPolicyProjection Project(AgentSelectorPolicyInput input)
    {
        var realItems = (input.Agents ?? Array.Empty<ServerConfiguration>())
            .Where(static agent => !string.IsNullOrWhiteSpace(agent.Id))
            .Select(agent => ComposerSelectorItemViewModel.Real(
                ComposerSelectorKind.Agent,
                agent.Id,
                string.IsNullOrWhiteSpace(agent.Name) ? agent.Id : agent.Name,
                input.Identity))
            .ToArray();

        if (input.IsConnecting)
        {
            return WithTopPlaceholder(input, realItems, SelectorPlaceholderKind.Loading, "Connecting agent...");
        }

        if (input.HasConnectionError)
        {
            return WithTopPlaceholder(input, realItems, SelectorPlaceholderKind.Error, "Agent unavailable");
        }

        if (!input.IsSelectionResolved)
        {
            return WithTopPlaceholder(input, realItems, SelectorPlaceholderKind.Unresolved, "Select an agent");
        }

        return new SelectorPolicyProjection(
            realItems,
            input.SelectedProfileId,
            Placeholder: null,
            ReplaceSelectionWithPlaceholder: false,
            DisableRealItems: false,
            SelectorEnabled: realItems.Length > 0);
    }

    private static SelectorPolicyProjection WithTopPlaceholder(
        AgentSelectorPolicyInput input,
        IReadOnlyList<ComposerSelectorItemViewModel> realItems,
        SelectorPlaceholderKind kind,
        string displayName)
    {
        var placeholder = ComposerSelectorItemViewModel.Placeholder(
            ComposerSelectorKind.Agent,
            kind,
            displayName,
            input.Identity,
            blocksSubmit: true);

        return new SelectorPolicyProjection(
            realItems,
            input.SelectedProfileId,
            placeholder,
            ReplaceSelectionWithPlaceholder: false,
            DisableRealItems: false,
            SelectorEnabled: true);
    }
}
