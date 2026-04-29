using System.Collections.Immutable;
using Moq;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Services.Chat;
using Uno.Extensions.Reactive;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

[Collection("NonParallel")]
public sealed class AuthoritativeRemoteSessionRouterTests
{
    [Fact]
    public async Task ResolveConversationIdAsync_WhenStoreBindingMatches_ReturnsConversationId()
    {
        var state = State.Value(this, () => ChatState.Empty with
        {
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty.Add(
                "conv-store",
                new ConversationBindingSlice("conv-store", "remote-store", "profile-1"))
        });
        var chatStore = new Mock<IChatStore>();
        chatStore.SetupGet(store => store.State).Returns(state);
        var router = new AuthoritativeRemoteSessionRouter(chatStore.Object);

        var conversationId = await router.ResolveConversationIdAsync("remote-store");

        Assert.Equal("conv-store", conversationId);
    }

    [Fact]
    public void ResolveConversationId_WhenStoreBindingMissing_ReturnsNull()
    {
        var router = new AuthoritativeRemoteSessionRouter(Mock.Of<IChatStore>());
        var state = ChatState.Empty with
        {
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty.Add(
                "conv-other",
                new ConversationBindingSlice("conv-other", "remote-other", "profile-1"))
        };

        var conversationId = router.ResolveConversationId(state, "remote-missing");

        Assert.Null(conversationId);
    }
}
