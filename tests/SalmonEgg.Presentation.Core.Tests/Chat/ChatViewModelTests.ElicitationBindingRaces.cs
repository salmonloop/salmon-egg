using Moq;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Models.Navigation;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public partial class ChatViewModelTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Elicitation_AcceptedUrlAuthorityChanges_CannotReopenOrSendAnotherResponse(bool connectionChanged)
    {
        // Arrange
        var dispatcher = new QueueingSynchronizationContext();
        var launcher = new Mock<IExternalUriLauncher>();
        launcher.SetupGet(value => value.IsSupported).Returns(true);
        launcher.Setup(value => value.OpenAsync(It.IsAny<ExternalUriTarget>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ExternalUriOpenResult.Dispatched);
        await using var fixture = CreateElicitationDeliveryFixture(dispatcher, launcher.Object);
        using var peer = await PermissionUiPeer.CreateAsync(UrlElicitationCapabilities());
        await AttachPermissionPeerAsync(fixture, dispatcher, peer);
        peer.Elicit("accepted", "url", "bound-session");
        await dispatcher.RunUntilIdleAsync();
        var card = fixture.ViewModel.PendingElicitationRequest!;
        await AwaitWithSynchronizationContextAsync(dispatcher, card.SubmitCommand.ExecuteAsync(null));
        Assert.True(card.CanReopen);

        // Act
        if (connectionChanged)
        {
            RegisterInteractionService(fixture, peer.Service, connectionId: "new-instance");
        }
        else
        {
            Assert.Equal(BindingUpdateStatus.Success, (await fixture.ViewModel.ConversationBindingCommands
                .UpdateBindingAsync("conv-1", "replacement-remote", "profile")).Status);
        }
        await card.ReopenCommand.ExecuteAsync(null);

        // Assert
        Assert.False(card.CanReopen);
        launcher.Verify(value => value.OpenAsync(It.IsAny<ExternalUriTarget>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("accept", Assert.Single(peer.Responses).GetProperty("result").GetProperty("action").GetString());
        await fixture.ApplyCurrentStoreProjectionAsync();
        await dispatcher.RunUntilIdleAsync();
        Assert.Null(fixture.ViewModel.PendingElicitationRequest);
    }

    [Fact]
    public async Task Elicitation_BindingCommitsBeforeUiProjection_CannotOpenUrlOrAccept()
    {
        // Arrange
        var dispatcher = new QueueingSynchronizationContext();
        var launcher = new Mock<IExternalUriLauncher>();
        launcher.SetupGet(value => value.IsSupported).Returns(true);
        launcher.Setup(value => value.OpenAsync(It.IsAny<ExternalUriTarget>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ExternalUriOpenResult.Dispatched);
        await using var fixture = CreateElicitationDeliveryFixture(dispatcher, launcher.Object);
        using var peer = await PermissionUiPeer.CreateAsync(UrlElicitationCapabilities());
        await AttachPermissionPeerAsync(fixture, dispatcher, peer);
        peer.Elicit("url", "url", "bound-session");
        await dispatcher.RunUntilIdleAsync();
        var card = fixture.ViewModel.PendingElicitationRequest!;
        Assert.True(card.CanSubmit);

        // Act: commit authoritative state without draining the UI dispatcher's older projection.
        var result = await fixture.ViewModel.ConversationBindingCommands
            .UpdateBindingAsync("conv-1", "replacement-remote", "profile");
        Assert.Equal(BindingUpdateStatus.Success, result.Status);
        await card.SubmitCommand.ExecuteAsync(null);

        // Assert: this synchronous guard runs before browser dispatch, preserving the click boundary.
        launcher.Verify(value => value.OpenAsync(It.IsAny<ExternalUriTarget>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.DoesNotContain(peer.Responses, response => response.GetProperty("result").GetProperty("action").GetString() == "accept");
        await dispatcher.RunUntilIdleAsync();
        Assert.Null(fixture.ViewModel.PendingElicitationRequest);
    }

    [Theory]
    [InlineData("form", true)]
    [InlineData("form", false)]
    [InlineData("url", true)]
    [InlineData("url", false)]
    public async Task Elicitation_BindingChangesWhileResponseWrites_KeepsDeliveryWithOriginalRequest(string mode, bool acceptSucceeds)
    {
        // Arrange
        var dispatcher = new QueueingSynchronizationContext();
        var launcher = new Mock<IExternalUriLauncher>();
        launcher.SetupGet(value => value.IsSupported).Returns(true);
        launcher.Setup(value => value.OpenAsync(It.IsAny<ExternalUriTarget>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ExternalUriOpenResult.Dispatched);
        await using var fixture = CreateElicitationDeliveryFixture(dispatcher, launcher.Object);
        using var peer = await PermissionUiPeer.CreateAsync(UrlElicitationCapabilities());
        await AttachPermissionPeerAsync(fixture, dispatcher, peer);
        peer.Elicit("form", mode, "bound-session");
        await dispatcher.RunUntilIdleAsync();
        var card = fixture.ViewModel.PendingElicitationRequest!;
        var started = NewPermissionSignal();
        var release = NewPermissionSignal();
        peer.ResponseSend = (response, token) =>
        {
            if (response.GetProperty("result").GetProperty("action").GetString() != "accept") return Task.FromResult(true);
            started.TrySetResult(true);
            return release.Task.WaitAsync(token);
        };
        var responseTask = card.SubmitCommand.ExecuteAsync(null);
        try
        {
            await AwaitPermissionUiSignalAsync(dispatcher, started.Task);

            // Act
            var result = await fixture.ViewModel.ConversationBindingCommands
                .UpdateBindingAsync("conv-1", "replacement-remote", "profile");
            Assert.Equal(BindingUpdateStatus.Success, result.Status);
            await dispatcher.RunUntilIdleAsync();

            // Assert: invalidation withdraws actions but does not tear down a live physical write.
            Assert.Same(card, fixture.ViewModel.PendingElicitationRequest);
            Assert.False(card.CanRespond);
            Assert.True(peer.IsConnected);
            Assert.Empty(peer.Responses);
            release.TrySetResult(acceptSucceeds);
            await AwaitPermissionUiSignalAsync(dispatcher, responseTask);
            await dispatcher.RunUntilIdleAsync();
            Assert.True(peer.IsConnected);
            Assert.Null(fixture.ViewModel.PendingElicitationRequest);
            Assert.Equal(acceptSucceeds ? "accept" : "cancel",
                Assert.Single(peer.Responses).GetProperty("result").GetProperty("action").GetString());
        }
        finally
        {
            release.TrySetResult(acceptSucceeds);
            await AwaitPermissionUiSignalAsync(dispatcher, responseTask);
        }
    }

    [Fact]
    public async Task Elicitation_BindingCancellationFailsAfterPoolSwitch_ClosesOnlyOriginalService()
    {
        // Arrange
        var dispatcher = new QueueingSynchronizationContext();
        var shell = new ShellNavigationRuntimeStateStore { CurrentShellContent = ShellNavigationContent.Chat };
        await using var fixture = CreateElicitationDeliveryFixture(dispatcher, shellNavigationRuntimeState: shell);
        using var oldPeer = await PermissionUiPeer.CreateAsync(UrlElicitationCapabilities());
        using var newPeer = await PermissionUiPeer.CreateAsync(UrlElicitationCapabilities());
        await AttachPermissionPeerAsync(fixture, dispatcher, oldPeer);
        RegisterInteractionService(fixture, newPeer.Service, "another-profile");
        oldPeer.Elicit("old", "form", "bound-session");
        await dispatcher.RunUntilIdleAsync();
        var started = NewPermissionSignal();
        var release = NewPermissionSignal();
        oldPeer.ResponseSend = (_, token) => { started.TrySetResult(true); return release.Task.WaitAsync(token); };
        var changed = await fixture.ViewModel.ConversationBindingCommands
            .UpdateBindingAsync("conv-1", "replacement-remote", "another-profile");
        Assert.Equal(BindingUpdateStatus.Success, changed.Status);
        try
        {
            await AwaitPermissionUiSignalAsync(dispatcher, started.Task);
            await AwaitWithSynchronizationContextAsync(dispatcher,
                fixture.ViewModel.ReplaceChatServiceWithIntentAsync(newPeer.Service, ServiceReplaceIntent.PoolOnly,
                    TestContext.Current.CancellationToken));
            shell.CurrentShellContent = ShellNavigationContent.Settings;

            // Act
            release.TrySetResult(false);
            await AwaitPermissionUiSignalAsync(dispatcher, oldPeer.ServiceDisposed.Task);
            await dispatcher.RunUntilIdleAsync();

            // Assert
            Assert.False(oldPeer.IsConnected);
            Assert.True(newPeer.IsConnected);
            Assert.Same(newPeer.Service, fixture.ViewModel.CurrentChatService);
            Assert.Empty(newPeer.Responses);
            Assert.Null((await fixture.ConnectionStore.GetCurrentStateAsync()).Error);
        }
        finally
        {
            release.TrySetResult(false);
            await dispatcher.RunUntilIdleAsync();
        }
    }

    [Fact]
    public async Task Elicitation_BindingCancellationWriteFails_RetainsOnlyCancellationForRetry()
    {
        // Arrange
        var dispatcher = new QueueingSynchronizationContext();
        var shell = new ShellNavigationRuntimeStateStore { CurrentShellContent = ShellNavigationContent.Chat };
        await using var fixture = CreateElicitationDeliveryFixture(dispatcher, shellNavigationRuntimeState: shell);
        using var peer = await PermissionUiPeer.CreateAsync(UrlElicitationCapabilities());
        await AttachPermissionPeerAsync(fixture, dispatcher, peer);
        peer.Elicit("old", "form", "bound-session");
        await dispatcher.RunUntilIdleAsync();
        var original = fixture.ViewModel.PendingElicitationRequest!;
        var attempts = 0;
        peer.ResponseSend = (_, _) => { attempts++; return Task.FromResult(false); };

        // Act
        var result = await fixture.ViewModel.ConversationBindingCommands
            .UpdateBindingAsync("conv-1", "replacement-remote", "profile");
        Assert.Equal(BindingUpdateStatus.Success, result.Status);
        await dispatcher.RunUntilIdleAsync();

        // Assert
        Assert.True(peer.IsConnected);
        Assert.Same(original, fixture.ViewModel.PendingElicitationRequest);
        Assert.False(original.CanRespond);
        Assert.True(original.CanCancel);
        Assert.True(original.HasError);
        await fixture.ApplyCurrentStoreProjectionAsync();
        await dispatcher.RunUntilIdleAsync();
        Assert.Equal(1, attempts);
        peer.ResponseSend = (_, _) => Task.FromResult(true);
        await AwaitWithSynchronizationContextAsync(dispatcher, original.CancelCommand.ExecuteAsync(null));
        Assert.Equal("cancel", Assert.Single(peer.Responses).GetProperty("result").GetProperty("action").GetString());
        Assert.Null(fixture.ViewModel.PendingElicitationRequest);
    }
}
