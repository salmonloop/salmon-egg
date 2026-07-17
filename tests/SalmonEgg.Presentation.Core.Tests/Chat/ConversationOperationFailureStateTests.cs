using SalmonEgg.Presentation.ViewModels.Chat;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public sealed class ConversationOperationFailureStateTests
{
    [Fact]
    public void ResolveVisibleMessage_OnlyReturnsFailureForItsOwner()
    {
        var state = new ConversationOperationFailureState();

        Assert.True(state.Publish("conv-a", "A failed", "conv-a"));

        Assert.Equal("A failed", state.ResolveVisibleMessage("conv-a"));
        Assert.Null(state.ResolveVisibleMessage("conv-b"));
        Assert.Null(state.ResolveVisibleMessage(null));
    }

    [Fact]
    public void Clear_OnlyClearsFailureForMatchingOwner()
    {
        var state = new ConversationOperationFailureState();
        Assert.True(state.Publish("conv-b", "B failed", "conv-b"));

        Assert.False(state.Clear("conv-a"));
        Assert.Equal("B failed", state.ResolveVisibleMessage("conv-b"));

        Assert.True(state.Clear("conv-b"));
        Assert.Null(state.ResolveVisibleMessage("conv-b"));
    }

    [Fact]
    public void OwnerlessFailure_IsVisibleOnlyWithoutCurrentConversation()
    {
        var state = new ConversationOperationFailureState();

        Assert.True(state.Publish(null, "Create failed before local conversation existed", null));

        Assert.Equal(
            "Create failed before local conversation existed",
            state.ResolveVisibleMessage(null));
        Assert.Null(state.ResolveVisibleMessage("conv-a"));
    }

    [Fact]
    public void Publish_LateOffscreenFailureDoesNotOverwriteCurrentOwnersFailure()
    {
        var state = new ConversationOperationFailureState();
        Assert.True(state.Publish("conv-b", "B failed", "conv-b"));

        Assert.False(state.Publish("conv-a", "A failed late", "conv-b"));

        Assert.Equal("B failed", state.ResolveVisibleMessage("conv-b"));
        Assert.Null(state.ResolveVisibleMessage("conv-a"));
    }
}
