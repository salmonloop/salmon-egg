using SalmonEgg.Presentation.ViewModels.Chat;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public sealed class ToolCallPillExpansionPolicyTests
{
    [Fact]
    public void ResolveDefaultExpanded_WhenToolCallIsIncomplete_DefaultsExpanded()
    {
        var isExpanded = ToolCallPillExpansionPolicy.ResolveDefaultExpanded(isCompleted: false);

        Assert.True(isExpanded);
    }

    [Fact]
    public void ResolveDefaultExpanded_WhenToolCallIsCompleted_DefaultsCollapsed()
    {
        var isExpanded = ToolCallPillExpansionPolicy.ResolveDefaultExpanded(isCompleted: true);

        Assert.False(isExpanded);
    }

    [Fact]
    public void ShouldShowInlineContent_WhenIncompleteButManuallyCollapsed_ReturnsFalse()
    {
        var shouldShow = ToolCallPillExpansionPolicy.ShouldShowInlineContent(
            hasInlineContent: true,
            isExpanded: false);

        Assert.False(shouldShow);
    }

    [Fact]
    public void ShouldShowInlineContent_WhenCompletedButManuallyExpanded_ReturnsTrue()
    {
        var shouldShow = ToolCallPillExpansionPolicy.ShouldShowInlineContent(
            hasInlineContent: true,
            isExpanded: true);

        Assert.True(shouldShow);
    }

    [Fact]
    public void ResolveEffectiveExpanded_WhenCompletedAndManuallyExpanded_PreservesManualExpansion()
    {
        var isExpanded = ToolCallPillExpansionPolicy.ResolveEffectiveExpanded(
            currentExpanded: true,
            isCompleted: true,
            hasManualOverride: true);

        Assert.True(isExpanded);
    }

    [Fact]
    public void ResolveEffectiveExpanded_WhenIncompleteAndManuallyCollapsed_PreservesManualCollapse()
    {
        var isExpanded = ToolCallPillExpansionPolicy.ResolveEffectiveExpanded(
            currentExpanded: false,
            isCompleted: false,
            hasManualOverride: true);

        Assert.False(isExpanded);
    }

    [Fact]
    public void ResolveEffectiveExpanded_WhenCompletedWithoutManualOverride_UsesDefaultCollapsed()
    {
        var isExpanded = ToolCallPillExpansionPolicy.ResolveEffectiveExpanded(
            currentExpanded: true,
            isCompleted: true,
            hasManualOverride: false);

        Assert.False(isExpanded);
    }

    [Fact]
    public void ResolveEffectiveExpanded_WhenIncompleteWithoutManualOverride_UsesDefaultExpanded()
    {
        var isExpanded = ToolCallPillExpansionPolicy.ResolveEffectiveExpanded(
            currentExpanded: false,
            isCompleted: false,
            hasManualOverride: false);

        Assert.True(isExpanded);
    }
}
