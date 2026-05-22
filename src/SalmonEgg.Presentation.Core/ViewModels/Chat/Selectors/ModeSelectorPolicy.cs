using System;
using System.Collections.Generic;
using System.Linq;
using SalmonEgg.Presentation.ViewModels.Chat;

namespace SalmonEgg.Presentation.Core.ViewModels.Chat.Selectors;

public sealed record ModeSelectorPolicyInput(
    string Identity,
    string CurrentIdentity,
    IReadOnlyList<SessionModeViewModel> Modes,
    string? SelectedModeId,
    bool IsAuthoritative,
    bool IsLoading,
    bool HasError,
    bool HasModeCapabilitySignal);

public sealed class ModeSelectorPolicy
{
    public SelectorPolicyProjection Project(ModeSelectorPolicyInput input)
    {
        var realItems = ToRealItems(input.Modes, input.CurrentIdentity);

        if (!string.Equals(input.Identity, input.CurrentIdentity, StringComparison.Ordinal))
        {
            return WithPlaceholder(
                realItems,
                input.SelectedModeId,
                SelectorPlaceholderKind.Unresolved,
                "Mode is not ready",
                input.CurrentIdentity,
                blocksSubmit: true);
        }

        if (input.IsLoading)
        {
            return WithPlaceholder(
                realItems,
                input.SelectedModeId,
                SelectorPlaceholderKind.Loading,
                "Loading modes...",
                input.CurrentIdentity,
                blocksSubmit: true);
        }

        if (input.HasError || (!input.IsAuthoritative && input.HasModeCapabilitySignal))
        {
            return WithPlaceholder(
                realItems,
                input.SelectedModeId,
                SelectorPlaceholderKind.Error,
                "Mode unavailable",
                input.CurrentIdentity,
                blocksSubmit: true);
        }

        if (!input.IsAuthoritative)
        {
            return WithPlaceholder(
                realItems,
                input.SelectedModeId,
                SelectorPlaceholderKind.Unresolved,
                "Mode is not ready",
                input.CurrentIdentity,
                blocksSubmit: true);
        }

        if (realItems.Count > 0)
        {
            return new SelectorPolicyProjection(
                realItems,
                input.SelectedModeId,
                Placeholder: null,
                ReplaceSelectionWithPlaceholder: false,
                DisableRealItems: false,
                SelectorEnabled: true);
        }

        if (!input.HasModeCapabilitySignal)
        {
            return WithPlaceholder(
                realItems,
                input.SelectedModeId,
                SelectorPlaceholderKind.Default,
                "Default mode",
                input.CurrentIdentity,
                blocksSubmit: false);
        }

        return WithPlaceholder(
            realItems,
            input.SelectedModeId,
            SelectorPlaceholderKind.Error,
            "Mode unavailable",
            input.CurrentIdentity,
            blocksSubmit: true);
    }

    private static IReadOnlyList<ComposerSelectorItemViewModel> ToRealItems(
        IEnumerable<SessionModeViewModel> modes,
        string identity)
        => modes
            .Where(static mode => !string.IsNullOrWhiteSpace(mode.ModeId))
            .Select(mode => ComposerSelectorItemViewModel.Real(
                ComposerSelectorKind.Mode,
                mode.ModeId,
                string.IsNullOrWhiteSpace(mode.ModeName) ? mode.ModeId : mode.ModeName,
                identity))
            .ToArray();

    private static SelectorPolicyProjection WithPlaceholder(
        IReadOnlyList<ComposerSelectorItemViewModel> realItems,
        string? selectedModeId,
        SelectorPlaceholderKind kind,
        string displayName,
        string identity,
        bool blocksSubmit)
    {
        var placeholder = ComposerSelectorItemViewModel.Placeholder(
            ComposerSelectorKind.Mode,
            kind,
            displayName,
            identity,
            blocksSubmit: blocksSubmit);

        return new SelectorPolicyProjection(
            realItems,
            selectedModeId,
            placeholder,
            ReplaceSelectionWithPlaceholder: true,
            DisableRealItems: true,
            SelectorEnabled: !blocksSubmit);
    }
}
