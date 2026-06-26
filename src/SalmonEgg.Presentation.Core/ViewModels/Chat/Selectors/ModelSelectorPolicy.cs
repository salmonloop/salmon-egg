using System;
using System.Collections.Generic;
using System.Linq;
using SalmonEgg.Presentation.ViewModels.Chat;

namespace SalmonEgg.Presentation.Core.ViewModels.Chat.Selectors;

public sealed record ModelSelectorPolicyInput(
    string Identity,
    string CurrentIdentity,
    IReadOnlyList<OptionValueViewModel> ModelOptions,
    string? SelectedModelValue,
    bool IsAuthoritative,
    bool IsLoading,
    bool HasError,
    ModelSelectorPlaceholderLabels Labels);

public sealed class ModelSelectorPolicy
{
    public SelectorPolicyProjection Project(ModelSelectorPolicyInput input)
    {
        var realItems = ToRealItems(input.ModelOptions, input.CurrentIdentity);

        if (!string.Equals(input.Identity, input.CurrentIdentity, StringComparison.Ordinal))
        {
            return WithPlaceholder(
                realItems,
                input.SelectedModelValue,
                SelectorPlaceholderKind.Unresolved,
                input.Labels.Unresolved,
                input.CurrentIdentity,
                blocksSubmit: false);
        }

        if (input.IsLoading)
        {
            return WithPlaceholder(
                realItems,
                input.SelectedModelValue,
                SelectorPlaceholderKind.Loading,
                input.Labels.Loading,
                input.CurrentIdentity,
                blocksSubmit: false);
        }

        if (input.HasError)
        {
            return WithPlaceholder(
                realItems,
                input.SelectedModelValue,
                SelectorPlaceholderKind.Error,
                input.Labels.Error,
                input.CurrentIdentity,
                blocksSubmit: false);
        }

        if (!input.IsAuthoritative)
        {
            return WithPlaceholder(
                realItems,
                input.SelectedModelValue,
                SelectorPlaceholderKind.Unresolved,
                input.Labels.Unresolved,
                input.CurrentIdentity,
                blocksSubmit: false);
        }

        if (realItems.Count > 0)
        {
            return new SelectorPolicyProjection(
                realItems,
                input.SelectedModelValue,
                Placeholder: null,
                ReplaceSelectionWithPlaceholder: false,
                DisableRealItems: false,
                SelectorEnabled: true);
        }

        return new SelectorPolicyProjection(
            realItems,
            input.SelectedModelValue,
            Placeholder: null,
            ReplaceSelectionWithPlaceholder: false,
            DisableRealItems: false,
            SelectorEnabled: false);
    }

    private static IReadOnlyList<ComposerSelectorItemViewModel> ToRealItems(
        IReadOnlyList<OptionValueViewModel> options,
        string identity)
        => options
            .Where(static option => !string.IsNullOrWhiteSpace(option.Value))
            .Select(option => ComposerSelectorItemViewModel.Real(
                ComposerSelectorKind.Model,
                option.Value,
                string.IsNullOrWhiteSpace(option.Name) ? option.Value : option.Name,
                identity))
            .ToArray();

    private static SelectorPolicyProjection WithPlaceholder(
        IReadOnlyList<ComposerSelectorItemViewModel> realItems,
        string? selectedValue,
        SelectorPlaceholderKind kind,
        string displayName,
        string identity,
        bool blocksSubmit)
    {
        var placeholder = ComposerSelectorItemViewModel.Placeholder(
            ComposerSelectorKind.Model,
            kind,
            displayName,
            identity,
            blocksSubmit: blocksSubmit);

        return new SelectorPolicyProjection(
            realItems,
            selectedValue,
            placeholder,
            ReplaceSelectionWithPlaceholder: true,
            DisableRealItems: true,
            SelectorEnabled: !blocksSubmit);
    }
}
