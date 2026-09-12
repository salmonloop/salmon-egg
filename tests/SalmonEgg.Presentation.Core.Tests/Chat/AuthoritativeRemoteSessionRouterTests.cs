using System.Collections.Immutable;
using Moq;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Domain.Models;
using SalmonEgg.Presentation.Core.Tests.Threading;
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
        var state = ChatState.Empty with
        {
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty.Add(
                "conv-store",
                new ConversationBindingSlice("conv-store", "remote-store", "profile-1"))
        };
        var chatStore = new Mock<IChatStore>();
        chatStore.Setup(store => store.GetCurrentStateAsync()).ReturnsAsync(state);
        var router = new AuthoritativeRemoteSessionRouter(chatStore.Object);

        var conversationId = await router.ResolveConversationIdAsync("remote-store", TestContext.Current.CancellationToken);

        Assert.Equal("conv-store", conversationId);
        chatStore.Verify(store => store.GetCurrentStateAsync(), Times.Once);
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

    [Fact]
    public void ResolveConversationId_WhenProfilesReuseRemoteId_UsesConnectionSourceAndRejectsAmbiguousLegacyLookup()
    {
        // Arrange
        var state = ChatState.Empty with
        {
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("a", new("a", "shared", "profile-a"))
                .Add("b", new("b", "shared", "profile-b"))
        };
        var registry = new InMemoryAcpConnectionSessionRegistry();
        var a = CreateSession("profile-a", "connection-a");
        var b = CreateSession("profile-b", "connection-b");
        registry.Upsert(a);
        registry.Upsert(b);
        var router = new AuthoritativeRemoteSessionRouter(Mock.Of<IChatStore>(), registry);

        // Act / Assert
        Assert.Equal("a", router.ResolveConversationId(state, "shared", a.EventSource));
        Assert.Equal("b", router.ResolveConversationId(state, "shared", b.EventSource));
        Assert.Null(router.ResolveConversationId(state, "shared"));
    }

    [Fact]
    public void ResolveConversationId_WhenConnectionIsReplaced_RejectsOldSourceAndMismatchedRuntime()
    {
        // Arrange
        var registry = new InMemoryAcpConnectionSessionRegistry();
        var oldSession = CreateSession("profile", "old");
        var newSession = CreateSession("profile", "new");
        registry.Upsert(oldSession);
        var router = new AuthoritativeRemoteSessionRouter(Mock.Of<IChatStore>(), registry);
        var state = ChatState.Empty with
        {
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty.Add("conversation", new("conversation", "remote", "profile")),
            RuntimeStates = ImmutableDictionary<string, ConversationRuntimeSlice>.Empty.Add("conversation",
                new("conversation", ConversationRuntimePhase.Warm, "old", "remote", "profile", "ready", DateTime.UtcNow))
        };

        // Act
        registry.Upsert(newSession);

        // Assert
        Assert.Null(router.ResolveConversationId(state, "remote", oldSession.EventSource));
        Assert.Null(router.ResolveConversationId(state, "remote", newSession.EventSource));
        state = state with { RuntimeStates = state.RuntimeStates!.SetItem("conversation", state.RuntimeStates["conversation"] with { ConnectionInstanceId = "new" }) };
        Assert.Equal("conversation", router.ResolveConversationId(state, "remote", newSession.EventSource));
    }

    [Fact]
    public void ResolveConversationId_WhenProfileBindingIsAmbiguous_DoesNotUseRuntimeToGuessItsOwner()
    {
        // Arrange
        var registry = new InMemoryAcpConnectionSessionRegistry();
        var session = CreateSession("profile", "current");
        registry.Upsert(session);
        var router = new AuthoritativeRemoteSessionRouter(Mock.Of<IChatStore>(), registry);
        var state = ChatState.Empty with
        {
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("a", new("a", "remote", "profile"))
                .Add("b", new("b", "remote", "profile")),
            RuntimeStates = ImmutableDictionary<string, ConversationRuntimeSlice>.Empty.Add("a",
                new("a", ConversationRuntimePhase.Warm, "old", "remote", "profile", "ready", DateTime.UtcNow))
        };

        // Act / Assert
        Assert.Null(router.ResolveConversationId(state, "remote", session.EventSource));
    }

    private static AcpConnectionSession CreateSession(string profile, string connection)
        => new(profile, new AcpChatServiceAdapter(Mock.Of<IChatService>(),
                new AcpEventAdapter(_ => { }, new ImmediateUiDispatcher())),
            new InitializeResponse(), new AcpConnectionReuseKey(TransportType.Stdio, profile, "", ""), connection);

    [Fact]
    public void ResolveConversationId_WhenDirectConnectionHasNoProfile_RequiresAnUnambiguousBinding()
    {
        // Arrange
        var router = new AuthoritativeRemoteSessionRouter(Mock.Of<IChatStore>(), new InMemoryAcpConnectionSessionRegistry());
        var source = new AcpSessionEventSource(null, "connection", Mock.Of<IChatService>());
        var state = ChatState.Empty with
        {
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty.Add("a", new("a", "remote", "profile-a"))
        };

        // Act / Assert
        Assert.Equal("a", router.ResolveConversationId(state, "remote", source));
        Assert.Null(router.ResolveConversationId(state with
        {
            Bindings = state.Bindings!.Add("b", new("b", "remote", "profile-b"))
        }, "remote", source));
    }
}
