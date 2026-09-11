using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Core.Tests.Localization;
using SalmonEgg.Presentation.Models.Navigation;
using SalmonEgg.Presentation.ViewModels.Chat;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public partial class ChatViewModelTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task PermissionDelivery_NoChatSurface_CancellationFailureEndsOriginalConnection(bool hasConversation, bool chatVisible)
    {
        // Arrange
        var dispatcher = new QueueingSynchronizationContext();
        var shell = new ShellNavigationRuntimeStateStore
        {
            CurrentShellContent = chatVisible ? ShellNavigationContent.Chat : ShellNavigationContent.Settings
        };
        var localizer = new MutableTestCoreStringLocalizer();
        localizer.Set("zh-Hans", "Permission_CancellationFailedDisconnected", "无法取消未显示的权限请求，已断开此连接。请重新连接 Agent。");
        var commands = new AcpChatCoordinator(Mock.Of<IAcpChatServiceFactory>(),
            NullLogger<AcpChatCoordinator>.Instance, Mock.Of<ITransportSupportPolicy>(),
            Mock.Of<IAcpMcpServerProvider>(), Mock.Of<IAcpSessionCommandOrchestrator>());
        await using var fixture = CreateInteractionViewModel(dispatcher, acpConnectionCommands: commands,
            shellNavigationRuntimeState: shell, localizer: localizer,
            acpConnectionCoordinatorFactory: store => new AcpConnectionCoordinator(store,
                NullLogger<AcpConnectionCoordinator>.Instance, Mock.Of<IAcpMcpServerResolver>(),
                Mock.Of<IAcpRemoteSessionRecoveryContextResolver>()));
        using var peer = await PermissionUiPeer.CreateAsync();
        await AttachPermissionPeerAsync(fixture, dispatcher, peer);
        if (!hasConversation)
        {
            await fixture.UpdateStateAsync(state => state with { HydratedConversationId = null });
        }
        peer.ResponseSend = (_, _) => Task.FromResult(false);

        // Act
        peer.Request("orphan", "unknown-session", "tool");
        await dispatcher.RunUntilIdleAsync();
        var state = await fixture.ConnectionStore.GetCurrentStateAsync();

        // Assert: the persistent connection error is observable outside the chat page.
        Assert.False(peer.IsConnected);
        Assert.False(Assert.Single(peer.Requests).CanRespond);
        Assert.Equal(ConnectionPhase.Disconnected, state.Phase);
        Assert.Equal("无法取消未显示的权限请求，已断开此连接。请重新连接 Agent。", state.Error);
        Assert.Null(fixture.ViewModel.PendingPermissionRequest);
        Assert.Null(fixture.ViewModel.CurrentChatService);
        Assert.Empty(peer.Responses);
    }

    [Fact]
    public async Task PermissionDelivery_NoChatSurface_DisconnectCompletesAfterReplacement_KeepsNewService()
    {
        // Arrange
        var dispatcher = new QueueingSynchronizationContext();
        var shell = new ShellNavigationRuntimeStateStore { CurrentShellContent = ShellNavigationContent.Settings };
        var commands = new AcpChatCoordinator(Mock.Of<IAcpChatServiceFactory>(),
            NullLogger<AcpChatCoordinator>.Instance, Mock.Of<ITransportSupportPolicy>(),
            Mock.Of<IAcpMcpServerProvider>(), Mock.Of<IAcpSessionCommandOrchestrator>());
        await using var fixture = CreateInteractionViewModel(dispatcher, acpConnectionCommands: commands,
            shellNavigationRuntimeState: shell,
            acpConnectionCoordinatorFactory: store => new AcpConnectionCoordinator(store,
                NullLogger<AcpConnectionCoordinator>.Instance, Mock.Of<IAcpMcpServerResolver>(),
                Mock.Of<IAcpRemoteSessionRecoveryContextResolver>()));
        using var oldPeer = await PermissionUiPeer.CreateAsync();
        using var currentPeer = await PermissionUiPeer.CreateAsync();
        await AttachPermissionPeerAsync(fixture, dispatcher, oldPeer);
        var started = NewPermissionSignal();
        var release = NewPermissionSignal();
        oldPeer.DisconnectSend = () => { started.TrySetResult(true); return release.Task; };
        oldPeer.ResponseSend = (_, _) => Task.FromResult(false);
        oldPeer.Request("same-id", "unknown-session", "old-tool");
        try
        {
            await AwaitPermissionUiSignalAsync(dispatcher, started.Task);

            // Act
            shell.CurrentShellContent = ShellNavigationContent.Chat;
            await AttachPermissionPeerAsync(fixture, dispatcher, currentPeer);
            currentPeer.Request("same-id", "remote-1", "new-tool");
            await dispatcher.RunUntilIdleAsync();
            var current = Assert.IsType<PermissionRequestViewModel>(fixture.ViewModel.PendingPermissionRequest);
            release.TrySetResult(true);
            await AwaitPermissionUiSignalAsync(dispatcher, oldPeer.ServiceDisposed.Task);
            await dispatcher.RunUntilIdleAsync();

            // Assert
            Assert.Same(currentPeer.Service, fixture.ViewModel.CurrentChatService);
            Assert.Same(current, fixture.ViewModel.PendingPermissionRequest);
            Assert.True(currentPeer.IsConnected);
            Assert.Null((await fixture.ConnectionStore.GetCurrentStateAsync()).Error);
            Assert.False(Assert.Single(oldPeer.Requests).CanRespond);
            Assert.Empty(currentPeer.Responses);
        }
        finally
        {
            release.TrySetResult(true);
            await AwaitPermissionUiSignalAsync(dispatcher, oldPeer.ServiceDisposed.Task);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PermissionDelivery_IndependentReplyInFlight_KeepsOriginalPromptUntilDelivery(bool succeeds)
    {
        // Arrange
        var dispatcher = new QueueingSynchronizationContext();
        await using var fixture = CreateInteractionViewModel(dispatcher);
        using var peer = await PermissionUiPeer.CreateAsync();
        await AttachPermissionPeerAsync(fixture, dispatcher, peer);
        peer.Request("first", "remote-1", "first-tool");
        peer.Request("second", "remote-1", "second-tool");
        await dispatcher.RunUntilIdleAsync();
        var first = fixture.ViewModel.PendingPermissionRequest!;
        var started = NewPermissionSignal();
        var release = NewPermissionSignal();
        peer.ResponseSend = (_, cancellationToken) =>
        {
            started.TrySetResult(true);
            return release.Task.WaitAsync(cancellationToken);
        };

        // Act
        var selectedOption = first.Options[0];
        var answering = selectedOption.SelectCommand.ExecuteAsync(null);
        try
        {
            await AwaitPermissionUiSignalAsync(dispatcher, started.Task);
            await dispatcher.RunUntilIdleAsync();

            // Assert: the native command keeps the original surface disabled until delivery.
            Assert.True(first.RespondCommand.IsRunning);
            Assert.False(first.RespondCommand.CanExecute(first.Options[0]));
            Assert.True(selectedOption.SelectCommand.IsRunning);
            Assert.False(selectedOption.SelectCommand.CanExecute(null));
            Assert.Empty(peer.Responses);
            Assert.Same(first, fixture.ViewModel.PendingPermissionRequest);
        }
        finally
        {
            release.TrySetResult(succeeds);
            await AwaitPermissionUiSignalAsync(dispatcher, answering);
            await dispatcher.RunUntilIdleAsync();
        }

        Assert.False(first.RespondCommand.IsRunning);
        if (succeeds)
        {
            Assert.Equal("first", Assert.Single(peer.Responses).GetProperty("id").GetString());
            Assert.Equal("second", fixture.ViewModel.PendingPermissionRequest!.MessageId.ToString());
        }
        else
        {
            Assert.Empty(peer.Responses);
            Assert.Same(first, fixture.ViewModel.PendingPermissionRequest);
            Assert.True(first.RespondCommand.CanExecute(first.Options[0]));
        }
    }

    [Fact]
    public async Task PermissionDelivery_UndisplayedCancellationFails_KeepsAnActionableRetryOwner()
    {
        // Arrange
        var dispatcher = new QueueingSynchronizationContext();
        var localizer = new MutableTestCoreStringLocalizer();
        localizer.Set("zh-Hans", "Permission_RetryCancellation", "重试取消");
        localizer.Set("zh-Hans", "Permission_Cancelled", "此请求已取消，请重试取消。");
        await using var fixture = CreateInteractionViewModel(dispatcher, localizer: localizer);
        using var peer = await PermissionUiPeer.CreateAsync();
        await AttachPermissionPeerAsync(fixture, dispatcher, peer);
        var attempts = 0;
        peer.ResponseSend = (_, _) =>
        {
            attempts++;
            return Task.FromResult(false);
        };

        // Act
        peer.Request("orphan", "unknown-session", "tool");
        await dispatcher.RunUntilIdleAsync();
        var request = Assert.Single(peer.Requests);

        // Assert
        Assert.Equal(1, attempts);
        Assert.Empty(peer.Responses);
        Assert.True(request.CanRespond);
        Assert.True(request.IsCancellationRequested);
        var retry = Assert.IsType<PermissionRequestViewModel>(fixture.ViewModel.PendingPermissionRequest);
        Assert.Same(retry, fixture.ViewModel.StandalonePermissionRequest);
        Assert.Equal("unknown-session", retry.SessionId);
        Assert.False(Assert.Single(retry.Options).IsAllow);
        Assert.Equal("重试取消", retry.Options[0].Name);
        Assert.Equal("此请求已取消，请重试取消。", retry.Description);
        Assert.True(fixture.ViewModel.ShowPermissionDialog);

        // A failed retry stays with this owner without a background retry loop.
        await AwaitWithSynchronizationContextAsync(dispatcher, retry.Options[0].SelectCommand.ExecuteAsync(null));
        await dispatcher.RunUntilIdleAsync();
        Assert.Same(retry, fixture.ViewModel.PendingPermissionRequest);
        Assert.Equal(2, attempts);
        Assert.Empty(peer.Responses);

        peer.ResponseSend = null;
        await AwaitWithSynchronizationContextAsync(dispatcher, retry.Options[0].SelectCommand.ExecuteAsync(null));
        await dispatcher.RunUntilIdleAsync();
        var response = Assert.Single(peer.Responses);
        Assert.Equal("orphan", response.GetProperty("id").GetString());
        Assert.Equal("cancelled", response.GetProperty("result").GetProperty("outcome").GetProperty("outcome").GetString());
        Assert.False(request.CanRespond);
        Assert.Null(fixture.ViewModel.PendingPermissionRequest);
        Assert.False(fixture.ViewModel.ShowPermissionDialog);
    }

    [Fact]
    public async Task PermissionDelivery_UnboundCancellationAndNewRequest_PreservesBothOwners()
    {
        // Arrange
        var dispatcher = new QueueingSynchronizationContext();
        await using var fixture = CreateInteractionViewModel(dispatcher);
        using var peer = await PermissionUiPeer.CreateAsync();
        await AttachPermissionPeerAsync(fixture, dispatcher, peer);
        peer.ResponseSend = (_, _) => Task.FromResult(false);
        peer.Request("orphan", "unknown-session", "orphan-tool");
        await dispatcher.RunUntilIdleAsync();
        var cancellation = Assert.IsType<PermissionRequestViewModel>(fixture.ViewModel.PendingPermissionRequest);

        // Act: a valid request can proceed while the original cancellation stays retryable.
        peer.ResponseSend = null;
        peer.Request("current", "remote-1", "current-tool");
        await dispatcher.RunUntilIdleAsync();
        var current = Assert.IsType<PermissionRequestViewModel>(fixture.ViewModel.PendingPermissionRequest);
        Assert.Equal("current", current.MessageId.ToString());
        await AwaitWithSynchronizationContextAsync(dispatcher, current.Options[0].SelectCommand.ExecuteAsync(null));
        await dispatcher.RunUntilIdleAsync();

        // Assert
        Assert.Same(cancellation, fixture.ViewModel.PendingPermissionRequest);
        Assert.False(Assert.Single(cancellation.Options).IsAllow);
        await AwaitWithSynchronizationContextAsync(dispatcher, cancellation.Options[0].SelectCommand.ExecuteAsync(null));
        Assert.Collection(peer.Responses,
            response => Assert.Equal("current", response.GetProperty("id").GetString()),
            response => Assert.Equal("orphan", response.GetProperty("id").GetString()));
        Assert.Null(fixture.ViewModel.PendingPermissionRequest);
    }

    [Fact]
    public async Task PermissionDelivery_UnboundCancellationDisconnects_RemovesRetryWithoutReplayingIt()
    {
        // Arrange
        var dispatcher = new QueueingSynchronizationContext();
        await using var fixture = CreateInteractionViewModel(dispatcher);
        using var peer = await PermissionUiPeer.CreateAsync();
        await AttachPermissionPeerAsync(fixture, dispatcher, peer);
        var attempts = 0;
        peer.ResponseSend = (_, _) => { attempts++; return Task.FromResult(false); };
        peer.Request("orphan", "unknown-session", "tool");
        await dispatcher.RunUntilIdleAsync();
        var retry = Assert.IsType<PermissionRequestViewModel>(fixture.ViewModel.PendingPermissionRequest);
        var removed = NewPermissionSignal();
        fixture.ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ChatViewModel.PendingPermissionRequest)
                && fixture.ViewModel.PendingPermissionRequest is null) removed.TrySetResult(true);
        };

        // Act
        await peer.DisconnectClientAsync();
        await AwaitPermissionUiSignalAsync(dispatcher, removed.Task);
        await AwaitWithSynchronizationContextAsync(dispatcher, retry.Options[0].SelectCommand.ExecuteAsync(null));

        // Assert
        Assert.Null(fixture.ViewModel.PendingPermissionRequest);
        Assert.Null(fixture.ViewModel.StandalonePermissionRequest);
        Assert.False(fixture.ViewModel.ShowPermissionDialog);
        Assert.Equal(1, attempts);
        Assert.Empty(peer.Responses);
    }

    [Fact]
    public async Task PermissionDelivery_UnboundCancellationServiceReplaced_DoesNotTouchReusedRequestId()
    {
        // Arrange
        var dispatcher = new QueueingSynchronizationContext();
        await using var fixture = CreateInteractionViewModel(dispatcher);
        using var oldPeer = await PermissionUiPeer.CreateAsync();
        using var currentPeer = await PermissionUiPeer.CreateAsync();
        await AttachPermissionPeerAsync(fixture, dispatcher, oldPeer);
        oldPeer.ResponseSend = (_, _) => Task.FromResult(false);
        oldPeer.Request("same-id", "unknown-session", "old-tool");
        await dispatcher.RunUntilIdleAsync();
        var retry = Assert.IsType<PermissionRequestViewModel>(fixture.ViewModel.PendingPermissionRequest);

        // Act
        await AttachPermissionPeerAsync(fixture, dispatcher, currentPeer);
        currentPeer.Request("same-id", "remote-1", "new-tool");
        await dispatcher.RunUntilIdleAsync();
        var current = Assert.IsType<PermissionRequestViewModel>(fixture.ViewModel.PendingPermissionRequest);
        await AwaitWithSynchronizationContextAsync(dispatcher, retry.Options[0].SelectCommand.ExecuteAsync(null));
        await dispatcher.RunUntilIdleAsync();

        // Assert
        Assert.NotSame(retry, current);
        Assert.Same(current, fixture.ViewModel.PendingPermissionRequest);
        Assert.Contains("new-tool", current.ToolCallJson);
        Assert.Empty(currentPeer.Responses);
        await AwaitWithSynchronizationContextAsync(dispatcher, current.Options[0].SelectCommand.ExecuteAsync(null));
        Assert.Equal("allow", Assert.Single(currentPeer.Responses).GetProperty("result")
            .GetProperty("outcome").GetProperty("optionId").GetString());
        Assert.Null(fixture.ViewModel.PendingPermissionRequest);
    }

    [Fact]
    public async Task PermissionDelivery_UnboundCancellationCompletesAfterServiceReplaced_DoesNotRestoreOldRetry()
    {
        // Arrange
        var dispatcher = new QueueingSynchronizationContext();
        await using var fixture = CreateInteractionViewModel(dispatcher);
        using var oldPeer = await PermissionUiPeer.CreateAsync();
        using var currentPeer = await PermissionUiPeer.CreateAsync();
        await AttachPermissionPeerAsync(fixture, dispatcher, oldPeer);
        var started = NewPermissionSignal();
        var release = NewPermissionSignal();
        oldPeer.ResponseSend = (_, token) =>
        {
            started.TrySetResult(true);
            return release.Task.WaitAsync(token);
        };
        oldPeer.Request("same-id", "unknown-session", "old-tool");
        try
        {
            await AwaitPermissionUiSignalAsync(dispatcher, started.Task);

            // Act
            await AttachPermissionPeerAsync(fixture, dispatcher, currentPeer);
            currentPeer.Request("same-id", "remote-1", "new-tool");
            await dispatcher.RunUntilIdleAsync();
            var current = Assert.IsType<PermissionRequestViewModel>(fixture.ViewModel.PendingPermissionRequest);
            release.TrySetResult(false);
            await dispatcher.RunUntilIdleAsync();

            // Assert
            Assert.Same(current, fixture.ViewModel.PendingPermissionRequest);
            Assert.Contains("new-tool", current.ToolCallJson);
            Assert.Empty(currentPeer.Responses);
        }
        finally
        {
            release.TrySetResult(false);
            await dispatcher.RunUntilIdleAsync();
        }
    }
}
