using System.Collections.Immutable;
using System.Threading.Tasks;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Services.Chat;
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
            SelectedProfileId = "profile-b"
        });
        var provider = new AcpConnectionDependencySnapshotProvider(
            new ChatStore(chatState),
            new ChatConnectionStore(connectionState));

        var snapshot = await provider.GetSnapshotAsync();

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

        var snapshot = await provider.GetSnapshotAsync();

        Assert.Null(snapshot.SelectedProfileId);
        Assert.Empty(snapshot.ProfilesRequiredByRemoteBindings);
    }
}
