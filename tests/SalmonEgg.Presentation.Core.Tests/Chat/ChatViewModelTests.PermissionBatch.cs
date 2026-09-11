using System.Collections.Concurrent;
using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SalmonEgg.Acp.Client;
using SalmonEgg.Acp.JsonRpc;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Acp.Serialization;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Core.Tests.Threading;
using SalmonEgg.Presentation.Models.Navigation;
using SalmonEgg.Presentation.ViewModels.Chat;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public partial class ChatViewModelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PermissionBatch_PhysicalArrayWrite_KeepsLastVisibleRequestUntilDelivery(bool succeeds)
    {
        // Arrange
        var dispatcher = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(dispatcher);
        using var stablePeer = await PermissionUiPeer.CreateAsync();
        using var peer = await DraftPermissionUiPeer.CreateAsync();
        await AttachPermissionPeerAsync(fixture, dispatcher, stablePeer, peer.Service);
        peer.RequestBatch("remote-1", "remote-1");
        await dispatcher.RunUntilIdleAsync();
        var first = fixture.ViewModel.PendingPermissionRequest!;
        var requests = peer.Requests.ToArray();
        var firstPrepared = WaitForPermissionStateAsync(requests[0], () => requests[0].IsResponsePrepared);
        var firstAnswer = first.RespondCommand.ExecuteAsync(first.Options[0]);
        var release = NewPermissionSignal();
        Task? secondAnswer = null;
        try
        {
            await AwaitPermissionUiSignalAsync(dispatcher, firstPrepared);
            Assert.False(requests[0].IsResponseSending);
            var secondVisible = WaitForPermissionProjectionAsync(fixture.ViewModel,
                () => fixture.ViewModel.PendingPermissionRequest?.MessageId.ToString() == "second");
            await AwaitPermissionUiSignalAsync(dispatcher, secondVisible);
            var second = fixture.ViewModel.PendingPermissionRequest!;
            var allSending = WaitForPermissionStateAsync(requests[0],
                () => requests.All(request => request.IsResponseSending));
            peer.ResponseSend = (_, token) => release.Task.WaitAsync(token);

            // Act
            secondAnswer = second.RespondCommand.ExecuteAsync(second.Options[0]);
            await AwaitPermissionUiSignalAsync(dispatcher, allSending);
            await dispatcher.RunUntilIdleAsync();

            // Assert
            Assert.Same(second, fixture.ViewModel.PendingPermissionRequest);
            Assert.All(requests, request => Assert.True(request.IsResponseSending));
            Assert.False(second.RespondCommand.CanExecute(second.Options[0]));
            Assert.Empty(peer.Responses);
            release.TrySetResult(succeeds);
            await AwaitPermissionUiSignalAsync(dispatcher, firstAnswer);
            await AwaitPermissionUiSignalAsync(dispatcher, secondAnswer);
            await dispatcher.RunUntilIdleAsync();
            if (succeeds)
            {
                Assert.Null(fixture.ViewModel.PendingPermissionRequest);
                Assert.Single(peer.Responses);
            }
            else
            {
                Assert.Same(first, fixture.ViewModel.PendingPermissionRequest);
                Assert.All(requests, request => Assert.False(request.IsResponseSending));
                Assert.True(first.RespondCommand.CanExecute(first.Options[0]));
                Assert.True(second.RespondCommand.CanExecute(second.Options[0]));
            }
        }
        finally
        {
            release.TrySetResult(false);
            await peer.DisconnectClientAsync();
            await AwaitPermissionUiSignalAsync(dispatcher, firstAnswer);
            if (secondAnswer is not null) await AwaitPermissionUiSignalAsync(dispatcher, secondAnswer);
        }
    }

    [Fact]
    public async Task PermissionBatch_UnboundCancellationAndFailedSibling_RetainsWholeArrayForRetry()
    {
        // Arrange
        var dispatcher = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(dispatcher);
        using var stablePeer = await PermissionUiPeer.CreateAsync();
        using var peer = await DraftPermissionUiPeer.CreateAsync();
        await AttachPermissionPeerAsync(fixture, dispatcher, stablePeer, peer.Service);
        peer.RequestBatch("unknown-session", "remote-1");
        var secondVisible = WaitForPermissionProjectionAsync(fixture.ViewModel,
            () => fixture.ViewModel.PendingPermissionRequest?.MessageId.ToString() == "second");
        await AwaitPermissionUiSignalAsync(dispatcher, secondVisible);
        var first = peer.Requests.First();
        var second = fixture.ViewModel.PendingPermissionRequest!;
        Assert.True(first.IsCancellationRequested);
        Assert.True(first.IsResponsePrepared);
        Assert.False(first.IsResponseSending);
        peer.FailNextResponse = true;

        // Act
        await AwaitPermissionUiSignalAsync(dispatcher, second.RespondCommand.ExecuteAsync(second.Options[0]));
        await dispatcher.RunUntilIdleAsync();

        // Assert: the original unbound cancellation remains one slot, never an authorization.
        Assert.True(first.CanRespond);
        Assert.False(first.IsResponseSending);
        Assert.Same(second, fixture.ViewModel.PendingPermissionRequest);
        Assert.Empty(peer.Responses);
        var release = NewPermissionSignal();
        peer.ResponseSend = (_, token) => release.Task.WaitAsync(token);
        var sending = WaitForPermissionStateAsync(first, () => first.IsResponseSending);
        var retry = second.RespondCommand.ExecuteAsync(second.Options[0]);
        try
        {
            await AwaitPermissionUiSignalAsync(dispatcher, sending);
            await dispatcher.RunUntilIdleAsync();
            Assert.Same(second, fixture.ViewModel.PendingPermissionRequest);
            Assert.Same(second, fixture.ViewModel.StandalonePermissionRequest);
            Assert.True(first.IsResponseSending);
            Assert.Empty(peer.Responses);
        }
        finally
        {
            release.TrySetResult(true);
            await AwaitPermissionUiSignalAsync(dispatcher, retry);
        }
        await dispatcher.RunUntilIdleAsync();
        var response = Assert.Single(peer.Responses);
        Assert.Equal("cancelled", response[0].GetProperty("result").GetProperty("outcome").GetProperty("outcome").GetString());
        Assert.Equal("selected", response[1].GetProperty("result").GetProperty("outcome").GetProperty("outcome").GetString());
        Assert.Null(fixture.ViewModel.PendingPermissionRequest);
        Assert.All(peer.Requests, request => Assert.False(request.CanRespond));
    }

    [Fact]
    public async Task PermissionBatch_NoChatSurfaceAndFailedCancellation_ClosesOnlyOriginalConnection()
    {
        // Arrange
        var dispatcher = new QueueingSynchronizationContext();
        var shell = new ShellNavigationRuntimeStateStore { CurrentShellContent = ShellNavigationContent.Settings };
        var commands = new AcpChatCoordinator(Mock.Of<IAcpChatServiceFactory>(),
            NullLogger<AcpChatCoordinator>.Instance, Mock.Of<ITransportSupportPolicy>(),
            Mock.Of<IAcpMcpServerProvider>(), Mock.Of<IAcpSessionCommandOrchestrator>());
        await using var fixture = CreateViewModel(dispatcher, acpConnectionCommands: commands,
            shellNavigationRuntimeState: shell,
            acpConnectionCoordinatorFactory: store => new AcpConnectionCoordinator(store,
                NullLogger<AcpConnectionCoordinator>.Instance, Mock.Of<IAcpMcpServerResolver>(),
                Mock.Of<IAcpRemoteSessionRecoveryContextResolver>()));
        using var stablePeer = await PermissionUiPeer.CreateAsync();
        using var peer = await DraftPermissionUiPeer.CreateAsync();
        await AttachPermissionPeerAsync(fixture, dispatcher, stablePeer, peer.Service);
        peer.FailNextResponse = true;

        // Act
        peer.RequestBatch("unknown-first", "unknown-second");
        await AwaitPermissionUiSignalAsync(dispatcher, peer.ServiceDisposed.Task);
        await dispatcher.RunUntilIdleAsync();

        // Assert
        Assert.False(peer.IsConnected);
        Assert.True(stablePeer.IsConnected);
        Assert.All(peer.Requests, request => Assert.False(request.CanRespond));
        Assert.Null(fixture.ViewModel.CurrentChatService);
        Assert.Null(fixture.ViewModel.PendingPermissionRequest);
        Assert.Empty(peer.Responses);
        Assert.Equal(ConnectionPhase.Disconnected, (await fixture.ConnectionStore.GetCurrentStateAsync()).Phase);
    }

    [Fact]
    public async Task PermissionBatch_CancelDuringPhysicalFailure_PreservesCurrentCardUntilCancellationWrites()
    {
        // Arrange
        var dispatcher = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(dispatcher);
        using var stablePeer = await PermissionUiPeer.CreateAsync();
        using var peer = await DraftPermissionUiPeer.CreateAsync();
        await AttachPermissionPeerAsync(fixture, dispatcher, stablePeer, peer.Service);
        peer.RequestBatch("remote-1", "remote-1");
        await dispatcher.RunUntilIdleAsync();
        var first = fixture.ViewModel.PendingPermissionRequest!;
        var requests = peer.Requests.ToArray();
        var next = WaitForPermissionProjectionAsync(fixture.ViewModel,
            () => fixture.ViewModel.PendingPermissionRequest?.MessageId.ToString() == "second");
        var firstAnswer = first.RespondCommand.ExecuteAsync(first.Options[0]);
        var write = NewPermissionSignal();
        var cancelWrite = NewPermissionSignal();
        var cancelStarted = NewPermissionSignal();
        Task? secondAnswer = null;
        var attempts = 0;
        try
        {
            await AwaitPermissionUiSignalAsync(dispatcher, next);
            var second = fixture.ViewModel.PendingPermissionRequest!;
            peer.ResponseSend = (_, token) =>
            {
                if (Interlocked.Increment(ref attempts) == 1) return write.Task.WaitAsync(token);
                cancelStarted.TrySetResult(true);
                return cancelWrite.Task.WaitAsync(token);
            };
            var sending = WaitForPermissionStateAsync(requests[1], () => requests[1].IsResponseSending);
            secondAnswer = second.RespondCommand.ExecuteAsync(second.Options[0]);
            await AwaitPermissionUiSignalAsync(dispatcher, sending);

            // Act
            peer.CancelPermission("first");
            write.TrySetResult(false);
            await AwaitPermissionUiSignalAsync(dispatcher, cancelStarted.Task);
            await dispatcher.RunUntilIdleAsync();

            // Assert
            Assert.Same(second, fixture.ViewModel.PendingPermissionRequest);
            Assert.All(requests, request => Assert.True(request.IsResponseSending));
            Assert.True(requests[0].IsCancellationRequested);
            Assert.Empty(peer.Responses);
            cancelWrite.TrySetResult(true);
            await AwaitPermissionUiSignalAsync(dispatcher, firstAnswer);
            await AwaitPermissionUiSignalAsync(dispatcher, secondAnswer);
            await dispatcher.RunUntilIdleAsync();
            var response = Assert.Single(peer.Responses);
            Assert.Equal(JsonRpcErrorCode.Cancelled, response[0].GetProperty("error").GetProperty("code").GetInt32());
            Assert.Equal("selected", response[1].GetProperty("result").GetProperty("outcome").GetProperty("outcome").GetString());
            Assert.Null(fixture.ViewModel.PendingPermissionRequest);
            Assert.Equal(2, attempts);
        }
        finally
        {
            write.TrySetResult(false);
            cancelWrite.TrySetResult(false);
            await peer.DisconnectClientAsync();
            await AwaitPermissionUiSignalAsync(dispatcher, firstAnswer);
            if (secondAnswer is not null) await AwaitPermissionUiSignalAsync(dispatcher, secondAnswer);
        }
    }

    [Fact]
    public async Task PermissionBatch_PreparedAnswersAdvanceAndFailedArrayRestoresOriginalRequests()
    {
        // Arrange
        var dispatcher = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(dispatcher);
        using var stablePeer = await PermissionUiPeer.CreateAsync();
        using var peer = await DraftPermissionUiPeer.CreateAsync();
        await AttachPermissionPeerAsync(fixture, dispatcher, stablePeer, peer.Service);
        peer.RequestBatch("remote-1", "remote-1");
        await dispatcher.RunUntilIdleAsync();
        var first = fixture.ViewModel.PendingPermissionRequest!;
        Assert.Equal("First permission", first.Title);
        Assert.Equal("Review first access", first.Description);
        var secondVisible = WaitForPermissionProjectionAsync(fixture.ViewModel, () =>
            fixture.ViewModel.PendingPermissionRequest?.MessageId.ToString() == "second");
        var firstAnswer = first.RespondCommand.ExecuteAsync(first.Options[0]);
        Task? secondAnswer = null;
        Task? retry = null;
        try
        {
            // Act: prepared is enough to show the next question, but does not confirm delivery.
            await AwaitPermissionUiSignalAsync(dispatcher, secondVisible);
            Assert.False(firstAnswer.IsCompleted);
            Assert.Empty(peer.Responses);
            var second = fixture.ViewModel.PendingPermissionRequest!;
            var restored = WaitForPermissionProjectionAsync(fixture.ViewModel,
                () => ReferenceEquals(first, fixture.ViewModel.PendingPermissionRequest));
            peer.FailNextResponse = true;
            secondAnswer = second.RespondCommand.ExecuteAsync(second.Options[0]);
            await AwaitPermissionUiSignalAsync(dispatcher, firstAnswer);
            await AwaitPermissionUiSignalAsync(dispatcher, secondAnswer);
            await AwaitPermissionUiSignalAsync(dispatcher, restored);

            // Assert: both original commands are retryable after the same failed physical write.
            Assert.True(first.RespondCommand.CanExecute(first.Options[0]));
            Assert.True(second.RespondCommand.CanExecute(second.Options[0]));
            Assert.Empty(peer.Responses);
            retry = first.RespondCommand.ExecuteAsync(first.Options[0]);
            await AwaitPermissionUiSignalAsync(dispatcher, retry);
            await dispatcher.RunUntilIdleAsync();
            var array = Assert.Single(peer.Responses);
            Assert.Equal(2, array.GetArrayLength());
            Assert.Equal("first", array[0].GetProperty("id").GetString());
            Assert.Equal("second", array[1].GetProperty("id").GetString());
            Assert.Null(fixture.ViewModel.PendingPermissionRequest);
            Assert.Null(first.UnsubscribeRequestChanges);
            Assert.Null(second.UnsubscribeRequestChanges);
        }
        finally
        {
            await peer.DisconnectClientAsync();
            await AwaitPermissionUiSignalAsync(dispatcher, firstAnswer);
            if (secondAnswer is not null) await AwaitPermissionUiSignalAsync(dispatcher, secondAnswer);
            if (retry is not null) await AwaitPermissionUiSignalAsync(dispatcher, retry);
        }
    }

    [Fact]
    public async Task PermissionBatch_BindingChangesWhileFirstAnswerPrepared_CancelsWithoutBlockingOtherConversation()
    {
        // Arrange
        var dispatcher = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(dispatcher);
        using var stablePeer = await PermissionUiPeer.CreateAsync();
        using var peer = await DraftPermissionUiPeer.CreateAsync();
        await AttachPermissionPeerAsync(fixture, dispatcher, stablePeer, peer.Service);
        peer.RequestBatch("remote-1", "remote-2");
        await dispatcher.RunUntilIdleAsync();
        var first = fixture.ViewModel.PendingPermissionRequest!;
        var prepared = WaitForPermissionProjectionAsync(fixture.ViewModel,
            () => fixture.ViewModel.PendingPermissionRequest is null);
        var firstAnswer = first.RespondCommand.ExecuteAsync(first.Options[0]);
        Task? secondAnswer = null;
        try
        {
            await AwaitPermissionUiSignalAsync(dispatcher, prepared);

            // Act: the first slot is withdrawn while the second conversation still owns its input.
            await fixture.UpdateStateAsync(state => state with
            {
                Bindings = state.Bindings!.SetItem("conv-1", new ConversationBindingSlice("conv-1", "replacement", "profile"))
            });
            await dispatcher.RunUntilIdleAsync();
            Assert.True(peer.Requests.First().IsCancellationRequested);
            Assert.False(firstAnswer.IsCompleted);
            Assert.Empty(peer.Responses);
            await SelectPermissionConversationAsync(fixture, "conv-2");
            var second = fixture.ViewModel.PendingPermissionRequest!;
            secondAnswer = second.RespondCommand.ExecuteAsync(second.Options[0]);
            await AwaitPermissionUiSignalAsync(dispatcher, secondAnswer);
            await AwaitPermissionUiSignalAsync(dispatcher, firstAnswer);

            // Assert
            var response = Assert.Single(peer.Responses);
            Assert.Equal("cancelled", response[0].GetProperty("result").GetProperty("outcome").GetProperty("outcome").GetString());
            Assert.Equal("selected", response[1].GetProperty("result").GetProperty("outcome").GetProperty("outcome").GetString());
            await SelectPermissionConversationAsync(fixture, "conv-1");
            Assert.Null(fixture.ViewModel.PendingPermissionRequest);
        }
        finally
        {
            await peer.DisconnectClientAsync();
            await AwaitPermissionUiSignalAsync(dispatcher, firstAnswer);
            if (secondAnswer is not null) await AwaitPermissionUiSignalAsync(dispatcher, secondAnswer);
        }
    }

    private static async Task WaitForPermissionProjectionAsync(ChatViewModel viewModel, Func<bool> predicate)
    {
        var changed = NewPermissionSignal();
        PropertyChangedEventHandler observer = (_, args) =>
        {
            if (args.PropertyName == nameof(ChatViewModel.PendingPermissionRequest) && predicate()) changed.TrySetResult(true);
        };
        viewModel.PropertyChanged += observer;
        try
        {
            if (predicate()) changed.TrySetResult(true);
            await changed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }
        finally
        {
            viewModel.PropertyChanged -= observer;
        }
    }

    private static async Task WaitForPermissionStateAsync(PermissionRequestEventArgs request, Func<bool> predicate)
    {
        var changed = NewPermissionSignal();
        EventHandler observer = (_, _) => { if (predicate()) changed.TrySetResult(true); };
        request.Changed += observer;
        try
        {
            if (predicate()) changed.TrySetResult(true);
            await changed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }
        finally
        {
            request.Changed -= observer;
        }
    }

    private sealed class DraftPermissionUiPeer : IAcpTransport
    {
        private readonly AcpClient _client;
        private bool _disposed;

        private DraftPermissionUiPeer()
        {
            _client = new AcpClient(this, Mock.Of<IAcpClientLogger>());
            _client.PermissionRequestReceived += (_, request) => Requests.Enqueue(request);
            Service = new AcpChatServiceAdapter(
                new ChatService(_client, Mock.Of<IErrorLogger>(), Mock.Of<ISessionManager>()),
                new AcpEventAdapter(_ => { }, new ImmediateUiDispatcher()));
        }

        internal AcpChatServiceAdapter Service { get; }
        internal ConcurrentQueue<PermissionRequestEventArgs> Requests { get; } = new();
        internal ConcurrentQueue<JsonElement> Responses { get; } = new();
        internal bool FailNextResponse { get; set; }
        internal Func<JsonElement, CancellationToken, Task<bool>>? ResponseSend { get; set; }
        internal TaskCompletionSource<bool> ServiceDisposed { get; } = NewPermissionSignal();
        public bool IsConnected { get; private set; }
        public event EventHandler<AcpTransportMessageReceivedEventArgs>? MessageReceived;
        public event EventHandler<AcpTransportErrorEventArgs>? ErrorOccurred { add { } remove { } }

        internal static async Task<DraftPermissionUiPeer> CreateAsync()
        {
            var peer = new DraftPermissionUiPeer();
            try
            {
                await peer._client.InitializeDraftAsync(new InitializeParams(new ClientInfo("Draft permission UI test", "1"),
                    new ClientCapabilities())
                { ProtocolVersion = AcpProtocolVersion.V2 }, TestContext.Current.CancellationToken)
                    .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
                return peer;
            }
            catch
            {
                peer.Dispose();
                throw;
            }
        }

        internal void RequestBatch(string firstSession, string secondSession)
            => Receive("[" + Request("first", firstSession, "First permission", "Review first access") + ","
                + Request("second", secondSession, "Second permission", "Review second access") + "]");

        internal Task<bool> DisconnectClientAsync() => _client.DisconnectAsync();

        internal void CancelPermission(string id)
            => Receive("{\"jsonrpc\":\"2.0\",\"method\":\"$/cancel_request\",\"params\":{\"requestId\":\"" + id + "\"}}");

        public Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IsConnected = true;
            return Task.FromResult(true);
        }

        public Task<bool> DisconnectAsync()
        {
            IsConnected = false;
            return Task.FromResult(true);
        }

        public Task<bool> SendMessageAsync(string message, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var document = JsonDocument.Parse(message);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("method", out var method))
            {
                if (method.GetString() == "initialize")
                {
                    var result = JsonSerializer.Serialize(new InitializeResponse(AcpProtocolVersion.V2,
                        new AgentInfo("Peer", "1"), new AgentCapabilities()),
                        AcpWireFormat.For(AcpProtocolVersion.V2).TypeInfo<InitializeResponse>());
                    Receive("{\"jsonrpc\":\"2.0\",\"id\":" + root.GetProperty("id").GetRawText()
                        + ",\"result\":" + result + "}");
                }
                return Task.FromResult(true);
            }
            if (FailNextResponse)
            {
                FailNextResponse = false;
                return Task.FromResult(false);
            }
            if (ResponseSend is { } send) return CompleteResponseAsync(root.Clone(), send, cancellationToken);
            Responses.Enqueue(root.Clone());
            return Task.FromResult(true);
        }

        private async Task<bool> CompleteResponseAsync(JsonElement response,
            Func<JsonElement, CancellationToken, Task<bool>> send, CancellationToken cancellationToken)
        {
            if (!await send(response, cancellationToken).ConfigureAwait(false)) return false;
            Responses.Enqueue(response);
            return true;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Service.Dispose();
            IsConnected = false;
            ServiceDisposed.TrySetResult(true);
        }

        private static string Request(string id, string session, string title, string description)
            => "{\"jsonrpc\":\"2.0\",\"id\":\"" + id + "\",\"method\":\"session/request_permission\",\"params\":{\"sessionId\":\""
                + session + "\",\"title\":\"" + title + "\",\"description\":\"" + description
                + "\",\"options\":[{\"optionId\":\"allow\",\"name\":\"Allow once\",\"kind\":\"allow_once\"}]}}";

        private void Receive(string message) => MessageReceived?.Invoke(this, new(message));
    }
}
