using System.Globalization;
using System.Resources;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Resources;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Core.Tests.Localization;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public partial class ChatViewModelTests
{
    private const string ElicitationDisconnectedMessage = "无法取消未能显示的请求，已断开此连接。请重新连接 Agent。";

    [Theory]
    [InlineData("url", "request")]
    [InlineData("url", "unbound-session")]
    [InlineData("form", "request")]
    [InlineData("form", "unbound-session")]
    public async Task ElicitationDelivery_UnshownCancellationFails_ClosesOriginalConnection(string mode, string scope)
    {
        // Arrange
        var dispatcher = new QueueingSynchronizationContext();
        await using var fixture = CreateElicitationDeliveryFixture(dispatcher);
        using var peer = await PermissionUiPeer.CreateAsync(UrlElicitationCapabilities());
        await AttachPermissionPeerAsync(fixture, dispatcher, peer);
        var attempts = 0;
        peer.ResponseSend = (_, _) => { attempts++; return Task.FromResult(false); };

        // Act
        peer.Elicit("unshown", mode, scope);
        await dispatcher.RunUntilIdleAsync();

        // Assert: the actual connection owner settles the request and publishes a localized fault.
        var state = await fixture.ConnectionStore.GetCurrentStateAsync();
        Assert.False(peer.IsConnected);
        Assert.False(Assert.Single(peer.Elicitations).State.CanCancel);
        Assert.Equal(ConnectionPhase.Disconnected, state.Phase);
        Assert.Equal(ElicitationDisconnectedMessage, state.Error);
        Assert.Null(fixture.ViewModel.CurrentChatService);
        Assert.Null(fixture.ViewModel.PendingElicitationRequest);
        Assert.Equal(1, attempts);
        Assert.Empty(peer.Responses);
    }

    [Theory]
    [InlineData("url", "request")]
    [InlineData("url", "unbound-session")]
    [InlineData("form", "request")]
    [InlineData("form", "unbound-session")]
    public async Task ElicitationDelivery_UnshownCancellationSucceeds_KeepsConnectionAvailable(string mode, string scope)
    {
        // Arrange
        var dispatcher = new QueueingSynchronizationContext();
        await using var fixture = CreateElicitationDeliveryFixture(dispatcher);
        using var peer = await PermissionUiPeer.CreateAsync(UrlElicitationCapabilities());
        await AttachPermissionPeerAsync(fixture, dispatcher, peer);

        // Act
        peer.Elicit("unshown", mode, scope);
        await dispatcher.RunUntilIdleAsync();

        // Assert
        Assert.True(peer.IsConnected);
        Assert.Same(peer.Service, fixture.ViewModel.CurrentChatService);
        Assert.False(Assert.Single(peer.Elicitations).State.CanCancel);
        Assert.Equal("cancel", Assert.Single(peer.Responses).GetProperty("result").GetProperty("action").GetString());
        Assert.Null((await fixture.ConnectionStore.GetCurrentStateAsync()).Error);
        peer.Elicit("next", "form", "bound-session");
        await dispatcher.RunUntilIdleAsync();
        Assert.Equal("next", fixture.ViewModel.PendingElicitationRequest?.MessageId.ToString());
    }

    [Fact]
    public async Task ElicitationDelivery_DecoratorForwardsInnerSender_ClosesSubscribedService()
    {
        // Arrange
        var dispatcher = new QueueingSynchronizationContext();
        await using var fixture = CreateElicitationDeliveryFixture(dispatcher);
        using var peer = await PermissionUiPeer.CreateAsync(UrlElicitationCapabilities());
        using var adapter = new AcpChatServiceAdapter(peer.Service, new AcpEventAdapter(_ => { }, dispatcher));
        await AttachPermissionPeerAsync(fixture, dispatcher, peer, adapter);
        peer.ResponseSend = (_, _) => Task.FromResult(false);

        // Act
        peer.Elicit("unshown", "url", "request");
        await dispatcher.RunUntilIdleAsync();

        // Assert: using the forwarded inner sender would fail this outer service identity check.
        Assert.Null(fixture.ViewModel.CurrentChatService);
        Assert.False(peer.IsConnected);
        Assert.Equal(ElicitationDisconnectedMessage, (await fixture.ConnectionStore.GetCurrentStateAsync()).Error);
    }

    [Fact]
    public async Task ElicitationDelivery_LoggerThrows_StillClosesUnresolvedConnection()
    {
        // Arrange
        var dispatcher = new QueueingSynchronizationContext();
        await using var fixture = CreateElicitationDeliveryFixture(dispatcher);
        using var peer = await PermissionUiPeer.CreateAsync(UrlElicitationCapabilities());
        await AttachPermissionPeerAsync(fixture, dispatcher, peer);
        fixture.ViewModelLogger.Setup(logger => logger.Log(
                LogLevel.Warning, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
            .Throws(new InvalidOperationException("Logger unavailable"));
        peer.ResponseSend = (_, _) => Task.FromException<bool>(new IOException("Transport send failed"));

        // Act
        peer.Elicit("unshown", "url", "unbound-session");
        await dispatcher.RunUntilIdleAsync();

        // Assert
        Assert.False(peer.IsConnected);
        Assert.Null(fixture.ViewModel.CurrentChatService);
        Assert.Equal(ElicitationDisconnectedMessage, (await fixture.ConnectionStore.GetCurrentStateAsync()).Error);
    }

    [Fact]
    public async Task ElicitationDelivery_CancellationFailsAfterReplacement_PreservesNewConnection()
    {
        // Arrange
        var dispatcher = new QueueingSynchronizationContext();
        await using var fixture = CreateElicitationDeliveryFixture(dispatcher);
        using var oldPeer = await PermissionUiPeer.CreateAsync(UrlElicitationCapabilities());
        using var currentPeer = await PermissionUiPeer.CreateAsync(UrlElicitationCapabilities());
        await AttachPermissionPeerAsync(fixture, dispatcher, oldPeer);
        var started = NewPermissionSignal();
        var release = NewPermissionSignal();
        var changed = NewPermissionSignal();
        oldPeer.ResponseSend = (_, token) => { started.TrySetResult(true); return release.Task.WaitAsync(token); };
        oldPeer.Elicit("same-id", "url", "request");
        Assert.Single(oldPeer.Elicitations).State.Changed += (_, _) => changed.TrySetResult(true);
        try
        {
            await AwaitPermissionUiSignalAsync(dispatcher, started.Task);
            await AttachPermissionPeerAsync(fixture, dispatcher, currentPeer);
            currentPeer.Elicit("same-id", "form", "bound-session");
            await dispatcher.RunUntilIdleAsync();
            var current = fixture.ViewModel.PendingElicitationRequest;
            Assert.NotNull(current);

            // Act
            release.TrySetResult(false);
            await AwaitPermissionUiSignalAsync(dispatcher, changed.Task);
            await dispatcher.RunUntilIdleAsync();

            // Assert
            Assert.Same(currentPeer.Service, fixture.ViewModel.CurrentChatService);
            Assert.Same(current, fixture.ViewModel.PendingElicitationRequest);
            Assert.True(currentPeer.IsConnected);
            Assert.False(currentPeer.ServiceDisposed.Task.IsCompleted);
            Assert.Null((await fixture.ConnectionStore.GetCurrentStateAsync()).Error);
            Assert.Empty(currentPeer.Responses);
        }
        finally
        {
            release.TrySetResult(false);
            await dispatcher.RunUntilIdleAsync();
        }
    }

    [Fact]
    public async Task ElicitationDelivery_DisconnectFinishesAfterReplacement_PreservesNewConnection()
    {
        // Arrange
        var dispatcher = new QueueingSynchronizationContext();
        await using var fixture = CreateElicitationDeliveryFixture(dispatcher);
        using var oldPeer = await PermissionUiPeer.CreateAsync(UrlElicitationCapabilities());
        using var currentPeer = await PermissionUiPeer.CreateAsync(UrlElicitationCapabilities());
        await AttachPermissionPeerAsync(fixture, dispatcher, oldPeer);
        var started = NewPermissionSignal();
        var release = NewPermissionSignal();
        oldPeer.DisconnectSend = () => { started.TrySetResult(true); return release.Task; };
        oldPeer.ResponseSend = (_, _) => Task.FromResult(false);
        oldPeer.Elicit("same-id", "url", "request");
        try
        {
            await AwaitPermissionUiSignalAsync(dispatcher, started.Task);
            await AttachPermissionPeerAsync(fixture, dispatcher, currentPeer);
            currentPeer.Elicit("same-id", "form", "bound-session");
            await dispatcher.RunUntilIdleAsync();
            var current = fixture.ViewModel.PendingElicitationRequest;

            // Act
            release.TrySetResult(true);
            await AwaitPermissionUiSignalAsync(dispatcher, oldPeer.ServiceDisposed.Task);
            await dispatcher.RunUntilIdleAsync();

            // Assert
            Assert.NotNull(current);
            Assert.Same(currentPeer.Service, fixture.ViewModel.CurrentChatService);
            Assert.Same(current, fixture.ViewModel.PendingElicitationRequest);
            Assert.True(currentPeer.IsConnected);
            Assert.Null((await fixture.ConnectionStore.GetCurrentStateAsync()).Error);
        }
        finally
        {
            release.TrySetResult(true);
            await AwaitPermissionUiSignalAsync(dispatcher, oldPeer.ServiceDisposed.Task);
        }
    }

    [Fact]
    public async Task ElicitationDelivery_CancellationFailsAfterPoolReplacement_ClosesOnlyOriginalConnection()
    {
        // Arrange
        var dispatcher = new QueueingSynchronizationContext();
        await using var fixture = CreateElicitationDeliveryFixture(dispatcher);
        using var oldPeer = await PermissionUiPeer.CreateAsync(UrlElicitationCapabilities());
        using var currentPeer = await PermissionUiPeer.CreateAsync(UrlElicitationCapabilities());
        await AttachPermissionPeerAsync(fixture, dispatcher, oldPeer);
        var started = NewPermissionSignal();
        var release = NewPermissionSignal();
        oldPeer.ResponseSend = (_, token) => { started.TrySetResult(true); return release.Task.WaitAsync(token); };
        oldPeer.Elicit("unshown", "url", "request");
        try
        {
            await AwaitPermissionUiSignalAsync(dispatcher, started.Task);
            await AwaitWithSynchronizationContextAsync(dispatcher,
                fixture.ViewModel.ReplaceChatServiceWithIntentAsync(currentPeer.Service, ServiceReplaceIntent.PoolOnly,
                    TestContext.Current.CancellationToken));
            Assert.True(oldPeer.IsConnected);

            // Act
            release.TrySetResult(false);
            await AwaitPermissionUiSignalAsync(dispatcher, oldPeer.ServiceDisposed.Task);
            await dispatcher.RunUntilIdleAsync();

            // Assert: same-generation background requests retain their captured connection owner.
            Assert.False(oldPeer.IsConnected);
            Assert.False(Assert.Single(oldPeer.Elicitations).State.CanCancel);
            Assert.Same(currentPeer.Service, fixture.ViewModel.CurrentChatService);
            Assert.True(currentPeer.IsConnected);
            Assert.Null((await fixture.ConnectionStore.GetCurrentStateAsync()).Error);
            Assert.Empty(currentPeer.Responses);
        }
        finally
        {
            release.TrySetResult(false);
            await dispatcher.RunUntilIdleAsync();
        }
    }

    [Fact]
    public async Task ElicitationDelivery_OccupiedSurfaceCannotCancelSecondRequest_ClosesItsConnection()
    {
        // Arrange
        var dispatcher = new QueueingSynchronizationContext();
        await using var fixture = CreateElicitationDeliveryFixture(dispatcher);
        using var peer = await PermissionUiPeer.CreateAsync(UrlElicitationCapabilities());
        await AttachPermissionPeerAsync(fixture, dispatcher, peer);
        peer.Elicit("visible", "form", "bound-session");
        await dispatcher.RunUntilIdleAsync();
        Assert.NotNull(fixture.ViewModel.PendingElicitationRequest);
        peer.ResponseSend = (_, _) => Task.FromResult(false);

        // Act
        peer.Elicit("unshown", "url", "bound-session");
        await dispatcher.RunUntilIdleAsync();

        // Assert
        Assert.False(peer.IsConnected);
        Assert.Null(fixture.ViewModel.CurrentChatService);
        Assert.All(peer.Elicitations, request => Assert.False(request.State.CanCancel));
        Assert.Equal(ElicitationDisconnectedMessage, (await fixture.ConnectionStore.GetCurrentStateAsync()).Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("zh-Hans")]
    [InlineData("en")]
    [InlineData("en-US")]
    public void ElicitationDelivery_ConnectionFailure_HasPackagedLocalizedMessage(string language)
    {
        // Arrange
        var resources = new ResourceManager(typeof(CoreStrings));

        // Act
        var message = resources.GetString("Elicitation_CancellationFailedDisconnected", CultureInfo.GetCultureInfo(language));

        // Assert
        Assert.Equal(language.StartsWith("en", StringComparison.Ordinal)
            ? "Could not cancel a request that cannot be displayed. The connection was closed. Reconnect to the agent."
            : ElicitationDisconnectedMessage, message);
    }

    private static ClientCapabilities UrlElicitationCapabilities()
        => ClientCapabilityDefaults.Create() with { Elicitation = new() { Form = new(), Url = new() } };

    private static ViewModelFixture CreateElicitationDeliveryFixture(QueueingSynchronizationContext dispatcher)
    {
        var localizer = new MutableTestCoreStringLocalizer();
        localizer.Set("zh-Hans", "Elicitation_CancellationFailedDisconnected", ElicitationDisconnectedMessage);
        var commands = new AcpChatCoordinator(Mock.Of<IAcpChatServiceFactory>(),
            NullLogger<AcpChatCoordinator>.Instance, Mock.Of<ITransportSupportPolicy>(),
            Mock.Of<IAcpMcpServerProvider>(), Mock.Of<IAcpSessionCommandOrchestrator>());
        return CreateViewModel(dispatcher, acpConnectionCommands: commands, localizer: localizer,
            acpConnectionCoordinatorFactory: store => new AcpConnectionCoordinator(store,
                NullLogger<AcpConnectionCoordinator>.Instance, Mock.Of<IAcpMcpServerResolver>(),
                Mock.Of<IAcpRemoteSessionRecoveryContextResolver>()));
    }
}
