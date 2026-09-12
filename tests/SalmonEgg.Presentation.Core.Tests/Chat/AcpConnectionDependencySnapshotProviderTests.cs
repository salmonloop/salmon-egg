using System.Collections.Immutable;
using System.Threading.Tasks;
using Moq;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.ViewModels.Chat;
using SalmonEgg.Presentation.ViewModels.Chat.Panels;
using Uno.Extensions.Reactive;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public sealed class AcpConnectionDependencySnapshotProviderTests
{
    [Fact]
    public async Task GetSnapshotAsync_CollectsProfilesRequiredByRemoteBindings()
    {
        var chatState = State.Value(new object(), () => ChatState.Empty with
        {
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-a", new ConversationBindingSlice("conv-a", "remote-a", "profile-a"))
                .Add("conv-b", new ConversationBindingSlice("conv-b", "remote-b", "profile-b"))
        });
        var connectionState = State.Value(new object(), () => ChatConnectionState.Empty with
        {
            ForegroundTransportProfileId = "profile-b"
        });
        var provider = new AcpConnectionDependencySnapshotProvider(
            new ChatStore(chatState),
            new ChatConnectionStore(connectionState));

        var snapshot = await provider.GetSnapshotAsync(TestContext.Current.CancellationToken);

        Assert.Equal("profile-b", snapshot.SelectedProfileId);
        Assert.Contains("profile-a", snapshot.ProfilesRequiredByRemoteBindings);
        Assert.Contains("profile-b", snapshot.ProfilesRequiredByRemoteBindings);
    }

    [Fact]
    public async Task GetSnapshotAsync_IgnoresBindingsWithoutRemoteSessionOrProfile()
    {
        var chatState = State.Value(new object(), () => ChatState.Empty with
        {
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-local", new ConversationBindingSlice("conv-local", null, "profile-a"))
                .Add("conv-unscoped", new ConversationBindingSlice("conv-unscoped", "remote-b", null))
        });
        var connectionState = State.Value(new object(), () => ChatConnectionState.Empty);
        var provider = new AcpConnectionDependencySnapshotProvider(
            new ChatStore(chatState),
            new ChatConnectionStore(connectionState));

        var snapshot = await provider.GetSnapshotAsync(TestContext.Current.CancellationToken);

        Assert.Null(snapshot.SelectedProfileId);
        Assert.Empty(snapshot.ProfilesRequiredByRemoteBindings);
    }

    [Fact]
    public async Task GetSnapshotAsync_WhenBackgroundTurnAndPendingRequestAreBusy_ProtectsTheirExactConnectionsAndReleasesOnCompletion()
    {
        // Arrange
        var turn = new ActiveTurnState("working", "turn", ChatTurnPhase.Thinking, DateTime.UtcNow, DateTime.UtcNow,
            ProfileId: "profile-a", RemoteSessionId: "remote-a", ConnectionInstanceId: "connection-a");
        var chatState = State.Value(new object(), () => ChatState.Empty with
        {
            Turns = ImmutableDictionary<string, ActiveTurnState>.Empty.Add("working", turn)
        });
        var connectionState = State.Value(new object(), () => ChatConnectionState.Empty);
        var panels = new ChatConversationPanelStateCoordinator();
        panels.StoreAskUserRequest("pending", new("question", "remote-b", "Question", [])
        {
            Source = new AcpSessionEventSource("profile-b", "connection-b", Mock.Of<IChatService>())
        });
        var store = new ChatStore(chatState);
        var provider = new AcpConnectionDependencySnapshotProvider(store, new ChatConnectionStore(connectionState), panels);

        // Act
        var busy = await provider.GetSnapshotAsync(TestContext.Current.CancellationToken);
        await store.Dispatch(new CompleteTurnAction("working", "turn", ConnectionInstanceId: turn.ConnectionInstanceId));
        panels.RemoveAskUserRequest("pending");
        var idle = await provider.GetSnapshotAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(("profile-a", "connection-a"), busy.BusyConnections);
        Assert.Contains(("profile-b", "connection-b"), busy.BusyConnections);
        Assert.Empty(idle.BusyConnections);
    }
}
