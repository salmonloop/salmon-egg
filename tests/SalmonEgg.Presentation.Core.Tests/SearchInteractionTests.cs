using SalmonEgg.Presentation.Logic;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests;

public class SearchInteractionTests
{
    private readonly SearchInteractionLogic _logic = new();

    [Theory]
    [InlineData(true, "", true)]   // Focused, no query -> Open
    [InlineData(false, "abc", true)] // Not focused, has query -> Open (e.g. typing)
    [InlineData(false, "", false)]  // Not focused, no query -> Closed
    public void ShouldOpen_FollowsBusinessRules(bool isFocused, string query, bool expected)
    {
        Assert.Equal(expected, _logic.ShouldOpen(isFocused, query));
    }

    [Theory]
    [InlineData(false, false, "", true)]  // None focused, no query -> Should Close
    [InlineData(true, false, "", false)]   // Search box focused -> Stay open
    [InlineData(false, true, "", false)]   // Popup focused -> Stay open
    [InlineData(false, false, "a", false)] // Has query -> Stay open
    public void ShouldClose_FollowsBusinessRules(bool isFocused, bool isPopupFocused, string query, bool expected)
    {
        Assert.Equal(expected, _logic.ShouldClose(isFocused, isPopupFocused, query));
    }
}
