using System;
using System.Collections.Generic;
using System.Linq;

namespace SalmonEgg.Presentation.Core.ViewModels.Chat.Selectors;

public sealed class SelectorProjectionPresenter
{
    public SelectorProjectionResult Present(SelectorProjectionInput input)
    {
        var realItems = input.RealItems ?? Array.Empty<ComposerSelectorItemViewModel>();
        var projectedRealItems = input.DisableRealItems
            ? realItems.Select(static item => item.AsDisabled()).ToArray()
            : realItems.ToArray();

        var selected = ResolveSelectedDisplayItem(input, projectedRealItems);

        // The placeholder appears as a dropdown row only when it is also the resolved closed-display
        // selection. A placeholder that does not occupy the selection slot is purely a status signal
        // (loading/error/unresolved while real items remain selectable); injecting it as a phantom row
        // above real items violates native dropdown semantics. Submit-block and placeholder-kind
        // signals continue to flow through the projection result fields below.
        var placeholderIsSelection = input.Placeholder is not null
            && ReferenceEquals(selected, input.Placeholder);
        var displayItems = placeholderIsSelection
            ? new[] { input.Placeholder! }.Concat(projectedRealItems).ToArray()
            : projectedRealItems;

        var isSubmitBlocked = input.Placeholder?.BlocksSubmit == true;
        var submitBlockReason = isSubmitBlocked
            ? input.Placeholder!.DisplayName
            : null;

        return new SelectorProjectionResult(
            displayItems,
            selected,
            input.SelectorEnabled,
            isSubmitBlocked,
            submitBlockReason,
            input.Placeholder?.PlaceholderKind ?? SelectorPlaceholderKind.None);
    }

    private static ComposerSelectorItemViewModel? ResolveSelectedDisplayItem(
        SelectorProjectionInput input,
        IReadOnlyList<ComposerSelectorItemViewModel> projectedRealItems)
    {
        if (input.Placeholder is not null && input.ReplaceSelectionWithPlaceholder)
        {
            return input.Placeholder;
        }

        if (string.IsNullOrWhiteSpace(input.SelectedSemanticValue))
        {
            return input.Placeholder;
        }

        return projectedRealItems.FirstOrDefault(item =>
            string.Equals(item.SemanticValue, input.SelectedSemanticValue, StringComparison.Ordinal));
    }
}
