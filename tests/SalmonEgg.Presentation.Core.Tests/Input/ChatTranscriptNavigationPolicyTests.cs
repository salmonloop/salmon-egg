using SalmonEgg.Presentation.Core.Services.Input;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Input;

public sealed class ChatTranscriptNavigationPolicyTests
{
    [Theory]
    [InlineData(GamepadNavigationIntent.MoveUp, -1)]
    [InlineData(GamepadNavigationIntent.MoveDown, 1)]
    public void Resolve_ReturnsRelativeScrollForVerticalTranscriptNavigation(
        GamepadNavigationIntent intent,
        int expectedItemDelta)
    {
        var decision = ChatTranscriptNavigationPolicy.Resolve(
            intent,
            hasTranscriptFocus: true,
            messageCount: 3);

        Assert.Equal(ChatTranscriptNavigationAction.ScrollByItems, decision.Action);
        Assert.Equal(expectedItemDelta, decision.ItemDelta);
    }

    [Theory]
    [InlineData(GamepadNavigationIntent.MoveLeft)]
    [InlineData(GamepadNavigationIntent.MoveRight)]
    [InlineData(GamepadNavigationIntent.Activate)]
    [InlineData(GamepadNavigationIntent.Back)]
    public void Resolve_DoesNotHijackNonScrollTranscriptIntents(GamepadNavigationIntent intent)
    {
        var decision = ChatTranscriptNavigationPolicy.Resolve(
            intent,
            hasTranscriptFocus: true,
            messageCount: 3);

        Assert.Equal(ChatTranscriptNavigationAction.None, decision.Action);
        Assert.Equal(0, decision.ItemDelta);
    }

    [Fact]
    public void Resolve_DoesNotConsumeWhenTranscriptIsNotFocused()
    {
        var decision = ChatTranscriptNavigationPolicy.Resolve(
            GamepadNavigationIntent.MoveDown,
            hasTranscriptFocus: false,
            messageCount: 3);

        Assert.Equal(ChatTranscriptNavigationAction.None, decision.Action);
    }

    [Fact]
    public void Resolve_DoesNotConsumeWhenTranscriptIsEmpty()
    {
        var decision = ChatTranscriptNavigationPolicy.Resolve(
            GamepadNavigationIntent.MoveDown,
            hasTranscriptFocus: true,
            messageCount: 0);

        Assert.Equal(ChatTranscriptNavigationAction.None, decision.Action);
    }

    [Fact]
    public void TryConsume_ScrollsTranscriptAndRegistersViewportIntent()
    {
        var scrolledDelta = 0;
        var registerCount = 0;

        var consumed = ChatTranscriptNavigationIntentHandler.TryConsume(
            GamepadNavigationIntent.MoveDown,
            hasTranscriptFocus: true,
            messageCount: 3,
            tryScrollByItems: delta =>
            {
                scrolledDelta = delta;
                return true;
            },
            registerUserViewportIntent: () => registerCount++);

        Assert.True(consumed);
        Assert.Equal(1, scrolledDelta);
        Assert.Equal(1, registerCount);
    }

    [Fact]
    public void TryConsume_DoesNotRegisterViewportIntentWhenScrollDoesNotMove()
    {
        var registerCount = 0;

        var consumed = ChatTranscriptNavigationIntentHandler.TryConsume(
            GamepadNavigationIntent.MoveUp,
            hasTranscriptFocus: true,
            messageCount: 3,
            tryScrollByItems: _ => false,
            registerUserViewportIntent: () => registerCount++);

        Assert.True(consumed);
        Assert.Equal(0, registerCount);
    }

    [Fact]
    public void TryConsume_DoesNotCallPlatformScrollWhenPolicyDoesNotConsume()
    {
        var scrollCount = 0;
        var registerCount = 0;

        var consumed = ChatTranscriptNavigationIntentHandler.TryConsume(
            GamepadNavigationIntent.MoveDown,
            hasTranscriptFocus: false,
            messageCount: 3,
            tryScrollByItems: _ =>
            {
                scrollCount++;
                return true;
            },
            registerUserViewportIntent: () => registerCount++);

        Assert.False(consumed);
        Assert.Equal(0, scrollCount);
        Assert.Equal(0, registerCount);
    }
}
