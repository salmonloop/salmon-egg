namespace SalmonEgg.Presentation.ViewModels.Chat;

/// <summary>
/// Defines the expansion contract for tool call inline details.
/// </summary>
public static class ToolCallPillExpansionPolicy
{
    /// <summary>
    /// Resolves the first expansion state before the user manually toggles details.
    /// </summary>
    /// <param name="isCompleted">Whether the tool call has reached a completed lifecycle state.</param>
    /// <returns><see langword="true" /> when details should initially be expanded.</returns>
    public static bool ResolveDefaultExpanded(bool isCompleted)
        => !isCompleted;

    /// <summary>
    /// Resolves the next expansion state when lifecycle or detail inputs refresh.
    /// </summary>
    /// <param name="currentExpanded">The current expansion state.</param>
    /// <param name="isCompleted">Whether the tool call has reached a completed lifecycle state.</param>
    /// <param name="hasManualOverride">Whether the user has manually toggled details.</param>
    /// <returns>The expansion state that should remain after the refresh.</returns>
    public static bool ResolveEffectiveExpanded(
        bool currentExpanded,
        bool isCompleted,
        bool hasManualOverride)
        => hasManualOverride ? currentExpanded : ResolveDefaultExpanded(isCompleted);

    /// <summary>
    /// Resolves whether inline details should be visible for the current user expansion state.
    /// </summary>
    /// <param name="hasInlineContent">Whether the pill has detail content to show.</param>
    /// <param name="isExpanded">Whether the pill is currently expanded.</param>
    /// <returns><see langword="true" /> when inline details should be visible.</returns>
    public static bool ShouldShowInlineContent(bool hasInlineContent, bool isExpanded)
        => hasInlineContent && isExpanded;
}
