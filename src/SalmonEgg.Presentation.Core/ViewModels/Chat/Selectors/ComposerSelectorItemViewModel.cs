namespace SalmonEgg.Presentation.Core.ViewModels.Chat.Selectors;

public sealed record ComposerSelectorItemViewModel(
    ComposerSelectorKind Kind,
    string DisplayName,
    string? SemanticValue,
    string Identity,
    bool IsPlaceholder,
    SelectorPlaceholderKind PlaceholderKind,
    bool IsSelectable,
    bool BlocksSubmit)
{
    public static ComposerSelectorItemViewModel Real(
        ComposerSelectorKind kind,
        string semanticValue,
        string displayName,
        string identity)
        => new(
            kind,
            displayName ?? string.Empty,
            semanticValue,
            identity ?? string.Empty,
            IsPlaceholder: false,
            SelectorPlaceholderKind.None,
            IsSelectable: true,
            BlocksSubmit: false);

    public static ComposerSelectorItemViewModel Placeholder(
        ComposerSelectorKind kind,
        SelectorPlaceholderKind placeholderKind,
        string displayName,
        string identity,
        string? semanticValue = null,
        bool blocksSubmit = true,
        bool isSelectable = false)
        => new(
            kind,
            displayName ?? string.Empty,
            semanticValue,
            identity ?? string.Empty,
            IsPlaceholder: true,
            placeholderKind,
            IsSelectable: isSelectable || !string.IsNullOrWhiteSpace(semanticValue),
            BlocksSubmit: blocksSubmit);

    public ComposerSelectorItemViewModel AsDisabled()
        => this with { IsSelectable = false };

    public string AutomationId
        => $"ComposerSelectorItem.{Kind}.{ResolveAutomationSegment()}";

    private string ResolveAutomationSegment()
    {
        if (!string.IsNullOrWhiteSpace(SemanticValue))
        {
            return SanitizeAutomationSegment(SemanticValue);
        }

        if (IsPlaceholder)
        {
            return PlaceholderKind.ToString();
        }

        return "Empty";
    }

    private static string SanitizeAutomationSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Empty";
        }

        var chars = value
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')
            .ToArray();
        return new string(chars);
    }
}
