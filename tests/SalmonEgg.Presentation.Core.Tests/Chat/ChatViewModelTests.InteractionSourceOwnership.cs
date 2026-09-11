using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Services.Chat;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public partial class ChatViewModelTests
{
    [Theory]
    [InlineData("form")]
    [InlineData("url")]
    public async Task Elicitation_BindingReplaced_WithdrawsTheOriginalRequest(string mode)
    {
        // Arrange
        var dispatcher = new QueueingSynchronizationContext();
        await using var fixture = CreateElicitationDeliveryFixture(dispatcher);
        using var peer = await PermissionUiPeer.CreateAsync(UrlElicitationCapabilities());
        await AttachPermissionPeerAsync(fixture, dispatcher, peer);
        peer.Elicit("old-form", mode, "bound-session");
        await dispatcher.RunUntilIdleAsync();
        var original = fixture.ViewModel.PendingElicitationRequest;
        Assert.NotNull(original);

        // Act: same local conversation now represents a different authoritative remote session.
        var changed = await fixture.ViewModel.ConversationBindingCommands
            .UpdateBindingAsync("conv-1", "replacement-remote", "profile");
        Assert.Equal(BindingUpdateStatus.Success, changed.Status);
        Assert.Equal("replacement-remote", (await fixture.ChatStore.GetCurrentStateAsync())
            .ResolveBinding("conv-1")?.RemoteSessionId);
        await dispatcher.RunUntilIdleAsync();

        // Assert: the stale question must not remain actionable on the replacement session.
        Assert.False(original.CanRespond);
        Assert.Null(fixture.ViewModel.PendingElicitationRequest);
        Assert.Equal("cancel", Assert.Single(peer.Responses).GetProperty("result").GetProperty("action").GetString());
    }

    [Fact]
    public async Task Permission_EqualRemoteIdsAcrossProfiles_DoNotGrantConsentToAnotherProfile()
    {
        // Arrange: the first real service publishes before a profile switch; routing waits while
        // the production BindingCoordinator gives a second profile its identically named session.
        var dispatcher = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(dispatcher);
        using var previousPeer = await PermissionUiPeer.CreateAsync();
        using var currentPeer = await PermissionUiPeer.CreateAsync();
        await AwaitWithSynchronizationContextAsync(dispatcher,
            fixture.Workspace.RestoreAsync(TestContext.Current.CancellationToken));
        await AttachPermissionPeerAsync(fixture, dispatcher, previousPeer);
        RegisterInteractionService(fixture, previousPeer.Service, "profile-a");
        RegisterInteractionService(fixture, currentPeer.Service, "profile-b");
        var first = await fixture.ViewModel.ConversationBindingCommands
            .UpdateBindingAsync("conv-1", "shared-remote", "profile-a");
        Assert.Equal(BindingUpdateStatus.Success, first.Status);
        await dispatcher.RunUntilIdleAsync();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.ChatStore.ReadState = async () =>
        {
            fixture.ChatStore.ReadState = null;
            started.TrySetResult();
            await release.Task.WaitAsync(TestContext.Current.CancellationToken);
            return fixture.ChatStore.LatestState;
        };
        previousPeer.Request("profile-a-permission", "shared-remote", "old-tool");
        await started.Task.WaitAsync(TestContext.Current.CancellationToken);
        try
        {
            var second = await fixture.ViewModel.ConversationBindingCommands
                .UpdateBindingAsync("conv-2", "shared-remote", "profile-b");
            TestContext.Current.TestOutputHelper!.WriteLine($"SecondBindingStatus={second.Status}; Error={second.ErrorMessage}");
            Assert.Equal(BindingUpdateStatus.Success, second.Status);
            var newState = await fixture.ChatStore.GetCurrentStateAsync();
            Assert.Null(newState.ResolveBinding("conv-1"));
            Assert.Equal("profile-b", newState.ResolveBinding("conv-2")?.ProfileId);
            await AwaitWithSynchronizationContextAsync(dispatcher,
                fixture.ViewModel.ReplaceChatServiceWithIntentAsync(currentPeer.Service,
                    ServiceReplaceIntent.PoolOnly, TestContext.Current.CancellationToken));
            await AwaitWithSynchronizationContextAsync(dispatcher,
                fixture.DispatchConnectionAsync(new SetSelectedProfileIntentAction("profile-b")).AsTask());
            await AwaitWithSynchronizationContextAsync(dispatcher,
                fixture.DispatchConnectionAsync(new SetForegroundTransportProfileAction("profile-b")).AsTask());
            await SelectPermissionConversationAsync(fixture, "conv-2");

            // Act: only now can the old request choose its destination from the new bindings.
            release.TrySetResult();
            await dispatcher.RunUntilIdleAsync();
            var projectedOnOtherProfile = fixture.ViewModel.PendingPermissionRequest;
            TestContext.Current.TestOutputHelper!.WriteLine(
                $"CurrentConversation={fixture.ViewModel.CurrentSessionId}; ForegroundProfile={fixture.ViewModel.ForegroundTransportProfileId}; "
                + $"VisibleRequest={projectedOnOtherProfile?.MessageId}; RequestBindingProfile={projectedOnOtherProfile?.Binding?.ProfileId}");
            if (projectedOnOtherProfile is not null)
            {
                await AwaitWithSynchronizationContextAsync(dispatcher,
                    projectedOnOtherProfile.RespondCommand.ExecuteAsync(projectedOnOtherProfile.Options[0]));
            }
            TestContext.Current.TestOutputHelper!.WriteLine(
                $"OldPeerReplies={string.Join(";", previousPeer.Responses.Select(response => response.GetRawText()))}; "
                + $"NewPeerReplyCount={currentPeer.Responses.Count}");

            // Assert: the current profile's interaction cannot grant consent to the previous peer.
            Assert.DoesNotContain(previousPeer.Responses, response => response.TryGetProperty("result", out var result)
                && result.TryGetProperty("outcome", out var outcome)
                && outcome.GetProperty("outcome").GetString() == "selected");
            Assert.Empty(currentPeer.Responses);
        }
        finally
        {
            fixture.ChatStore.ReadState = null;
            release.TrySetResult();
            await dispatcher.RunUntilIdleAsync();
        }
    }
}
