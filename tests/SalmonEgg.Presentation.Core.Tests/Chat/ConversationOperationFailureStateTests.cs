using System.Collections.Immutable;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public sealed class ConversationOperationFailureStateTests
{
    [Fact]
    public void ResolveVisibleMessage_OnlyReturnsFailureForItsOwner()
    {
        var state = ChatReducer.Reduce(ChatState.Empty,
            new SetConversationOperationFailureAction(new("conv-a", "A failed")));

        Assert.Equal("A failed", state.ResolveOperationFailure("conv-a")?.Message);
        Assert.Null(state.ResolveOperationFailure("conv-b"));
        Assert.Null(state.ResolveOperationFailure(null));
    }

    [Fact]
    public void Clear_OnlyClearsFailureForMatchingOwner()
    {
        var state = ChatReducer.Reduce(ChatState.Empty,
            new SetConversationOperationFailureAction(new("conv-b", "B failed")));

        state = ChatReducer.Reduce(state, new ClearConversationOperationFailureAction("conv-a"));
        Assert.Equal("B failed", state.ResolveOperationFailure("conv-b")?.Message);

        state = ChatReducer.Reduce(state, new ClearConversationOperationFailureAction("conv-b"));
        Assert.Null(state.ResolveOperationFailure("conv-b"));
    }

    [Fact]
    public void Publish_WithoutConversation_LeavesSessionStateUnchanged()
    {
        var state = ChatReducer.Reduce(ChatState.Empty,
            new SetConversationOperationFailureAction(new(null, "Configuration error")));

        Assert.Same(ChatState.Empty, state);
    }

    [Fact]
    public void Publish_LateOffscreenFailure_PreservesBothConversationFailures()
    {
        var state = ChatReducer.Reduce(ChatState.Empty,
            new SetConversationOperationFailureAction(new("conv-b", "B failed")));
        state = ChatReducer.Reduce(state, new SetConversationOperationFailureAction(new("conv-a", "A failed late")));

        Assert.Equal("B failed", state.ResolveOperationFailure("conv-b")?.Message);
        Assert.Equal("A failed late", state.ResolveOperationFailure("conv-a")?.Message);
    }

    [Fact]
    public void Publish_PreservesLocalizationIdentityForReproject()
    {
        var state = ChatReducer.Reduce(ChatState.Empty, new SetConversationOperationFailureAction(new(
            "conv-a",
            "Failed to switch mode: boom",
            ResourceKey: "ChatOperation_SwitchModeFailed",
            Fallback: "Failed to switch mode: {0}",
            FormatArgs: ImmutableArray.Create<object>("boom"))));

        var failure = Assert.IsType<ConversationOperationFailure>(state.ResolveOperationFailure("conv-a"));
        Assert.Equal("conv-a", failure.ConversationId);
        Assert.Equal("ChatOperation_SwitchModeFailed", failure.ResourceKey);
        Assert.Equal("Failed to switch mode: {0}", failure.Fallback);
        Assert.Equal("boom", Assert.Single(failure.FormatArgs));
        Assert.Equal("Failed to switch mode: boom", failure.Message);
    }

    [Fact]
    public void Publish_RawMessageHasNoResourceKey()
    {
        var state = ChatReducer.Reduce(ChatState.Empty,
            new SetConversationOperationFailureAction(new("conv-a", "Transport failed")));

        var failure = Assert.IsType<ConversationOperationFailure>(state.ResolveOperationFailure("conv-a"));
        Assert.Null(failure.ResourceKey);
        Assert.Null(failure.Fallback);
        Assert.True(failure.FormatArgs.IsDefaultOrEmpty);
    }

    [Fact]
    public void PublishAndClear_OnlyChangeRuntimeFactsWithoutSchedulingWorkspacePersistence()
    {
        // Arrange
        var original = ChatState.Empty with { Generation = 23 };

        // Act
        var failed = ChatReducer.Reduce(original, new SetConversationOperationFailureAction(new("conversation", "Failure")));
        var cleared = ChatReducer.Reduce(failed, new ClearConversationOperationFailureAction("conversation"));

        // Assert
        Assert.Equal(original.Generation, failed.Generation);
        Assert.Equal(original.Generation, cleared.Generation);
        Assert.NotNull(failed.ResolveOperationFailure("conversation"));
        Assert.Null(cleared.ResolveOperationFailure("conversation"));
    }
}
