using System.Collections.Concurrent;
using System.Text.Json;
using SalmonEgg.Acp.Client;
using SalmonEgg.Acp.JsonRpc;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Acp.Serialization;

namespace SalmonEgg.Acp.Tests.Client;

public sealed class AcpClientPermissionCancellationOwnershipTests
{
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(5);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitCancellation_DuringSelectionWrite_LatchesWithoutRacingTerminalResponses(bool selectionWriteSucceeds)
    {
        // Arrange
        using var peer = await PermissionPeer.CreateAsync();
        peer.Request();
        var request = Assert.Single(peer.Requests);
        var started = NewSignal<bool>();
        var release = NewSignal<bool>();
        peer.ResponseSend = (response, token) =>
        {
            if (response.GetProperty("result").GetProperty("outcome").GetProperty("outcome").GetString() != "selected")
                return Task.FromResult(true);
            started.TrySetResult(true);
            return release.Task.WaitAsync(token);
        };
        var answer = request.TryRespondAsync("selected", "allow");
        try
        {
            await started.Task.WaitAsync(WaitTimeout, TestToken);

            // Act: withdrawing the choice must not wait for a response that may need another input.
            Assert.False(await request.TryRespondAsync("cancelled").WaitAsync(WaitTimeout, TestToken));
            Assert.True(request.IsCancellationRequested);
            Assert.Single(peer.Attempts);
            release.TrySetResult(selectionWriteSucceeds);
            Assert.Equal(selectionWriteSucceeds, await answer.WaitAsync(WaitTimeout, TestToken));
            await peer.FirstResponseWritten.WaitAsync(WaitTimeout, TestToken);

            // Assert
            var response = Assert.Single(peer.Responses);
            Assert.Equal(selectionWriteSucceeds ? "selected" : "cancelled",
                response.GetProperty("result").GetProperty("outcome").GetProperty("outcome").GetString());
            Assert.Equal(selectionWriteSucceeds ? 1 : 2, peer.Attempts.Count);
            Assert.False(request.CanRespond);
            Assert.False(await request.TryRespondAsync("selected", "allow").WaitAsync(WaitTimeout, TestToken));
        }
        finally
        {
            release.TrySetResult(selectionWriteSucceeds);
            await answer.WaitAsync(WaitTimeout, TestToken);
        }
    }

    [Fact]
    public async Task ExplicitCancellation_FailedWrite_CannotBeReplacedBySelection()
    {
        // Arrange
        using var peer = await PermissionPeer.CreateAsync();
        peer.Request();
        var request = Assert.Single(peer.Requests);
        peer.ResponseSend = (_, _) => Task.FromResult(false);

        // Act
        Assert.False(await request.TryRespondAsync("cancelled").WaitAsync(WaitTimeout, TestToken));

        // Assert
        Assert.True(request.IsCancellationRequested);
        Assert.True(request.CanRespond);
        Assert.False(await request.TryRespondAsync("selected", "allow").WaitAsync(WaitTimeout, TestToken));
        Assert.Single(peer.Attempts);
        peer.ResponseSend = null;
        Assert.True(await request.TryRespondAsync("cancelled").WaitAsync(WaitTimeout, TestToken));
        Assert.Equal("cancelled", Assert.Single(peer.Responses).GetProperty("result").GetProperty("outcome").GetProperty("outcome").GetString());
        Assert.False(request.CanRespond);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PeerCancellation_FailedWrite_PreservesOwnerUntilExplicitRetry(bool retryFromOriginalRequest)
    {
        // Arrange
        using var peer = await PermissionPeer.CreateAsync();
        peer.Request();
        var request = Assert.Single(peer.Requests);
        peer.ResponseSend = (_, _) => Task.FromResult(false);

        // Act
        peer.CancelRequest();

        // Assert: a failed cancellation still owns its exact request, but cannot turn into a selection.
        Assert.Equal(JsonRpcErrorCode.Cancelled,
            Assert.Single(peer.Attempts).GetProperty("error").GetProperty("code").GetInt32());
        Assert.Empty(peer.Responses);
        Assert.True(request.CanRespond);
        Assert.False(await request.TryRespondAsync("selected", "allow").WaitAsync(WaitTimeout, TestToken));
        Assert.Single(peer.Attempts);

        peer.ResponseSend = null;
        if (retryFromOriginalRequest)
        {
            Assert.True(await request.TryRespondAsync("cancelled").WaitAsync(WaitTimeout, TestToken));
        }
        else
        {
            peer.CancelRequest();
        }
        await peer.FirstResponseWritten.WaitAsync(WaitTimeout, TestToken);

        var response = Assert.Single(peer.Responses);
        Assert.Equal("81", response.GetProperty("id").GetRawText());
        Assert.Equal(JsonRpcErrorCode.Cancelled, response.GetProperty("error").GetProperty("code").GetInt32());
        Assert.False(response.TryGetProperty("result", out _));
        Assert.False(request.CanRespond);
        Assert.False(await request.TryRespondAsync("cancelled").WaitAsync(WaitTimeout, TestToken));
        peer.CancelRequest();
        Assert.Equal(2, peer.Attempts.Count);
        Assert.Single(peer.Responses);
        Assert.Empty(peer.Errors);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PeerCancellation_DuringSelectionWrite_SendsExactlyOneTerminalResponse(bool selectionWriteSucceeds)
    {
        // Arrange
        using var peer = await PermissionPeer.CreateAsync();
        peer.Request();
        var request = Assert.Single(peer.Requests);
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        peer.ResponseSend = (response, token) =>
        {
            if (response.TryGetProperty("result", out _))
            {
                started.TrySetResult(true);
                return release.Task.WaitAsync(token);
            }
            return Task.FromResult(true);
        };
        var answer = request.TryRespondAsync("selected", "allow");
        try
        {
            await started.Task.WaitAsync(WaitTimeout, TestToken);
            Assert.Single(peer.Attempts);

            // Act: cancellation cannot race a second terminal response against the original write.
            peer.CancelRequest();
            Assert.Single(peer.Attempts);
            Assert.Empty(peer.Responses);
            release.TrySetResult(selectionWriteSucceeds);
            await answer.WaitAsync(WaitTimeout, TestToken);
            await peer.FirstResponseWritten.WaitAsync(WaitTimeout, TestToken);

            // Assert
            var response = Assert.Single(peer.Responses);
            Assert.Equal("81", response.GetProperty("id").GetRawText());
            if (selectionWriteSucceeds)
            {
                Assert.Equal("selected", response.GetProperty("result").GetProperty("outcome").GetProperty("outcome").GetString());
                Assert.False(response.TryGetProperty("error", out _));
            }
            else
            {
                Assert.Equal(JsonRpcErrorCode.Cancelled, response.GetProperty("error").GetProperty("code").GetInt32());
                Assert.False(response.TryGetProperty("result", out _));
            }
            Assert.False(request.CanRespond);
            Assert.False(await request.TryRespondAsync("selected", "allow").WaitAsync(WaitTimeout, TestToken));
            peer.CancelRequest();
            Assert.Equal(selectionWriteSucceeds ? 1 : 2, peer.Attempts.Count);
            Assert.Single(peer.Responses);
            Assert.Empty(peer.Errors);
        }
        finally
        {
            release.TrySetResult(selectionWriteSucceeds);
            await answer.WaitAsync(WaitTimeout, TestToken);
        }
    }

    [Fact]
    public async Task Changed_ThrowingSubscriber_DoesNotSkipCompletionForNextSubscriber()
    {
        // Arrange
        using var peer = await PermissionPeer.CreateAsync();
        peer.Request();
        var request = Assert.Single(peer.Requests);
        var throwingObserverCalled = NewSignal<bool>();
        var completed = NewSignal<(object? Sender, bool Prepared, bool Cancelled)>();
        EventHandler throwingObserver = (_, _) =>
        {
            throwingObserverCalled.TrySetResult(true);
            throw new InvalidOperationException("The host observer failed.");
        };
        EventHandler nextObserver = (sender, _) =>
        {
            if (!request.CanRespond)
            {
                completed.TrySetResult((sender, request.IsResponsePrepared, request.IsCancellationRequested));
            }
        };
        request.Changed += throwingObserver;
        request.Changed += nextObserver;
        try
        {
            // Act
            var sent = await request.TryRespondAsync("selected", "allow").WaitAsync(WaitTimeout, TestToken);
            await throwingObserverCalled.Task.WaitAsync(WaitTimeout, TestToken);
            var observed = await completed.Task.WaitAsync(WaitTimeout, TestToken);

            // Assert
            Assert.True(sent);
            Assert.Same(request, observed.Sender);
            Assert.False(observed.Prepared);
            Assert.False(observed.Cancelled);
            Assert.Single(peer.Responses);
            Assert.Empty(peer.Errors);
        }
        finally
        {
            request.Changed -= throwingObserver;
            request.Changed -= nextObserver;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Changed_BlockingSubscriber_DoesNotHoldResponseCompletion(bool blockOnPreparation)
    {
        // Arrange
        using var peer = await PermissionPeer.CreateAsync();
        peer.Request();
        var request = Assert.Single(peer.Requests);
        var writeStarted = NewSignal<bool>();
        var releaseWrite = NewSignal<bool>();
        var observerEntered = NewSignal<bool>();
        var observerReturned = NewSignal<bool>();
        using var releaseObserver = new ManualResetEventSlim();
        peer.ResponseSend = (_, token) =>
        {
            writeStarted.TrySetResult(true);
            return releaseWrite.Task.WaitAsync(token);
        };
        EventHandler blockingObserver = (_, _) =>
        {
            if ((blockOnPreparation ? !request.IsResponsePrepared : request.CanRespond)
                || !observerEntered.TrySetResult(true)) return;
            try
            {
                // Deliberately simulate a blocking host. The bounded wait is released in finally,
                // including when a regression stalls the response, so no test thread is left behind.
                observerReturned.TrySetResult(releaseObserver.Wait(WaitTimeout + WaitTimeout, TestToken));
            }
            catch (Exception error)
            {
                observerReturned.TrySetException(error);
            }
        };
        request.Changed += blockingObserver;
        var answer = Task.Run(() => request.TryRespondAsync("selected", "allow"), TestToken);
        try
        {
            // Act
            await writeStarted.Task.WaitAsync(WaitTimeout, TestToken);
            if (blockOnPreparation) await observerEntered.Task.WaitAsync(WaitTimeout, TestToken);
            releaseWrite.TrySetResult(true);
            await observerEntered.Task.WaitAsync(WaitTimeout, TestToken);
            await peer.FirstResponseWritten.WaitAsync(WaitTimeout, TestToken);
            var sent = await answer.WaitAsync(WaitTimeout, TestToken);

            // Assert: both the physical write and its caller finish while the observer still waits.
            Assert.True(sent);
            Assert.False(observerReturned.Task.IsCompleted);
            Assert.False(request.CanRespond);
            Assert.False(request.IsResponsePrepared);
            Assert.Single(peer.Responses);
            Assert.Empty(peer.Errors);
        }
        finally
        {
            releaseWrite.TrySetResult(true);
            releaseObserver.Set();
            request.Changed -= blockingObserver;
            await answer.WaitAsync(WaitTimeout, TestToken);
            if (observerEntered.Task.IsCompletedSuccessfully)
            {
                await observerReturned.Task.WaitAsync(WaitTimeout, TestToken);
            }
        }
    }

    [Fact]
    public async Task Changed_CompletesDuringPreparedNotification_NotifiesTerminalStateOnLaterCallback()
    {
        // Arrange
        using var peer = await PermissionPeer.CreateAsync();
        peer.Request();
        var request = Assert.Single(peer.Requests);
        var writeStarted = NewSignal<bool>();
        var releaseWrite = NewSignal<bool>();
        var prepared = NewSignal<(bool CanRespond, bool Prepared)>();
        var firstObserverReturned = NewSignal<bool>();
        var terminal = NewSignal<(int Entry, bool CanRespond, bool Prepared)>();
        using var releaseObserver = new ManualResetEventSlim();
        var entries = 0;
        peer.ResponseSend = (_, token) =>
        {
            writeStarted.TrySetResult(true);
            return releaseWrite.Task.WaitAsync(token);
        };
        EventHandler observer = (_, _) =>
        {
            var entry = Interlocked.Increment(ref entries);
            var state = (request.CanRespond, request.IsResponsePrepared);
            if (entry != 1)
            {
                if (!state.CanRespond) terminal.TrySetResult((entry, state.CanRespond, state.IsResponsePrepared));
                return;
            }
            prepared.TrySetResult(state);
            try
            {
                // Freeze this callback after its snapshot. Only a later callback may observe the
                // terminal state, proving a transition during notification is not lost to coalescing.
                firstObserverReturned.TrySetResult(releaseObserver.Wait(WaitTimeout + WaitTimeout, TestToken));
            }
            catch (Exception error)
            {
                firstObserverReturned.TrySetException(error);
            }
        };
        request.Changed += observer;
        var answer = Task.Run(() => request.TryRespondAsync("selected", "allow"), TestToken);
        try
        {
            // Act
            await writeStarted.Task.WaitAsync(WaitTimeout, TestToken);
            Assert.Equal((true, true), await prepared.Task.WaitAsync(WaitTimeout, TestToken));
            releaseWrite.TrySetResult(true);
            await peer.FirstResponseWritten.WaitAsync(WaitTimeout, TestToken);
            Assert.True(await answer.WaitAsync(WaitTimeout, TestToken));
            Assert.False(firstObserverReturned.Task.IsCompleted);
            Assert.False(terminal.Task.IsCompleted);
            releaseObserver.Set();
            var observed = await terminal.Task.WaitAsync(WaitTimeout, TestToken);

            // Assert
            Assert.True(observed.Entry >= 2);
            Assert.False(observed.CanRespond);
            Assert.False(observed.Prepared);
            Assert.Single(peer.Responses);
            Assert.Empty(peer.Errors);
        }
        finally
        {
            releaseWrite.TrySetResult(true);
            releaseObserver.Set();
            request.Changed -= observer;
            await answer.WaitAsync(WaitTimeout, TestToken);
            if (prepared.Task.IsCompletedSuccessfully)
                await firstObserverReturned.Task.WaitAsync(WaitTimeout, TestToken);
        }
    }

    [Fact]
    public async Task Changed_PeerCancellationWriteFails_NotifiesRetryAndOriginalCallbackCompletion()
    {
        // Arrange
        using var peer = await PermissionPeer.CreateAsync();
        peer.Request();
        var request = Assert.Single(peer.Requests);
        var writeStarted = NewSignal<bool>();
        var releaseFailure = NewSignal<bool>();
        var preparing = NewSignal<bool>();
        var retryable = NewSignal<(bool CanRespond, bool Prepared, bool Cancelled)>();
        var completed = NewSignal<(bool CanRespond, bool Prepared, bool Cancelled)>();
        peer.ResponseSend = (_, token) =>
        {
            writeStarted.TrySetResult(true);
            return releaseFailure.Task.WaitAsync(token);
        };
        EventHandler observer = (_, _) =>
        {
            var state = (request.CanRespond, request.IsResponsePrepared, request.IsCancellationRequested);
            if (state is (true, true, true)) preparing.TrySetResult(true);
            if (state is (true, false, true)) retryable.TrySetResult(state);
            if (!state.CanRespond) completed.TrySetResult(state);
        };
        request.Changed += observer;
        try
        {
            // Act
            peer.CancelRequest();
            await writeStarted.Task.WaitAsync(WaitTimeout, TestToken);
            await preparing.Task.WaitAsync(WaitTimeout, TestToken);
            releaseFailure.TrySetResult(false);
            var failedState = await retryable.Task.WaitAsync(WaitTimeout, TestToken);

            // Assert: a failed peer cancellation exposes only cancellation retry, retaining its id.
            Assert.Equal((true, false, true), failedState);
            Assert.Single(peer.Attempts);
            Assert.Empty(peer.Responses);

            peer.ResponseSend = null;
            Assert.True(await request.TryRespondAsync("cancelled").WaitAsync(WaitTimeout, TestToken));
            var finalState = await completed.Task.WaitAsync(WaitTimeout, TestToken);
            Assert.Equal((false, false, true), finalState);
            var response = Assert.Single(peer.Responses);
            Assert.Equal("81", response.GetProperty("id").GetRawText());
            Assert.Equal(JsonRpcErrorCode.Cancelled, response.GetProperty("error").GetProperty("code").GetInt32());
            Assert.False(response.TryGetProperty("result", out _));
            Assert.Equal(2, peer.Attempts.Count);
            Assert.Empty(peer.Errors);
        }
        finally
        {
            releaseFailure.TrySetResult(false);
            request.Changed -= observer;
        }
    }

    [Fact]
    public async Task Changed_ConnectionCloses_NotifiesOriginalRequestIsUnavailable()
    {
        // Arrange
        using var peer = await PermissionPeer.CreateAsync();
        peer.Request();
        var request = Assert.Single(peer.Requests);
        var closed = NewSignal<(object? Sender, bool CanRespond, bool Prepared)>();
        EventHandler observer = (sender, _) =>
        {
            if (!request.CanRespond)
            {
                closed.TrySetResult((sender, request.CanRespond, request.IsResponsePrepared));
            }
        };
        request.Changed += observer;
        try
        {
            // Act
            var disconnected = await peer.Client.DisconnectAsync().WaitAsync(WaitTimeout, TestToken);
            var observed = await closed.Task.WaitAsync(WaitTimeout, TestToken);

            // Assert
            Assert.True(disconnected);
            Assert.Same(request, observed.Sender);
            Assert.False(observed.CanRespond);
            Assert.False(observed.Prepared);
            Assert.False(await request.TryRespondAsync("cancelled").WaitAsync(WaitTimeout, TestToken));
            Assert.Empty(peer.Attempts);
            Assert.Empty(peer.Responses);
            Assert.Empty(peer.Errors);
        }
        finally
        {
            request.Changed -= observer;
        }
    }

    [Theory]
    [InlineData("\"81\"")]
    [InlineData("true")]
    [InlineData("{}")]
    [InlineData("null")]
    public async Task PeerCancellation_MismatchedIdKindOrInvalidId_LeavesOriginalPermissionAvailable(string requestId)
    {
        // Arrange
        using var peer = await PermissionPeer.CreateAsync();
        peer.Request();
        var request = Assert.Single(peer.Requests);

        // Act
        peer.CancelRequest(requestId);

        // Assert
        Assert.True(request.CanRespond);
        Assert.False(request.IsCancellationRequested);
        Assert.Empty(peer.Attempts);
        Assert.True(await request.TryRespondAsync("selected", "allow").WaitAsync(WaitTimeout, TestToken));
        Assert.Equal("selected", Assert.Single(peer.Responses).GetProperty("result")
            .GetProperty("outcome").GetProperty("outcome").GetString());
        Assert.Empty(peer.Errors);
    }

    [Fact]
    public async Task PeerCancellation_CapturedOldConnectionCallback_DoesNotCancelReusedRequestId()
    {
        // Arrange
        using var peer = await PermissionPeer.CreateAsync();
        peer.Request();
        var original = Assert.Single(peer.Requests);
        var oldReceiver = peer.CaptureReceiver();
        Assert.NotNull(oldReceiver);
        Assert.True(await peer.Client.DisconnectAsync().WaitAsync(WaitTimeout, TestToken));
        await peer.InitializeAsync();
        peer.Request();
        var replacement = peer.Requests.Last();

        // Act
        oldReceiver(peer, new AcpTransportMessageReceivedEventArgs(
            """{"jsonrpc":"2.0","method":"$/cancel_request","params":{"requestId":81}}"""));

        // Assert
        Assert.False(original.CanRespond);
        Assert.True(replacement.CanRespond);
        Assert.False(replacement.IsCancellationRequested);
        Assert.Empty(peer.Attempts);
        Assert.True(await replacement.TryRespondAsync("selected", "allow").WaitAsync(WaitTimeout, TestToken));
        Assert.Single(peer.Responses);
        Assert.Empty(peer.Errors);
    }

    [Fact]
    public async Task Changed_RequestIdReplaced_NotifiesOnlyOriginalRequestIsUnavailable()
    {
        // Arrange
        using var peer = await PermissionPeer.CreateAsync();
        peer.Request();
        var original = Assert.Single(peer.Requests);
        var replaced = NewSignal<bool>();
        EventHandler observer = (_, _) =>
        {
            if (!original.CanRespond) replaced.TrySetResult(true);
        };
        original.Changed += observer;
        try
        {
            // Act
            peer.Request();
            await replaced.Task.WaitAsync(WaitTimeout, TestToken);
            var replacement = peer.Requests.Last();

            // Assert
            Assert.False(original.CanRespond);
            Assert.False(await original.TryRespondAsync("cancelled").WaitAsync(WaitTimeout, TestToken));
            Assert.True(replacement.CanRespond);
            Assert.Empty(peer.Attempts);
            Assert.True(await replacement.TryRespondAsync("selected", "allow").WaitAsync(WaitTimeout, TestToken));
            Assert.Single(peer.Responses);
            Assert.Empty(peer.Errors);
        }
        finally
        {
            original.Changed -= observer;
        }
    }

    private static TaskCompletionSource<T> NewSignal<T>()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class PermissionPeer : IAcpTransport
    {
        private readonly TaskCompletionSource<bool> _firstResponseWritten = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _disposed;

        private PermissionPeer()
        {
            Client = new AcpClient(this);
            Client.PermissionRequestReceived += (_, request) => Requests.Enqueue(request);
            Client.ErrorOccurred += (_, error) => Errors.Enqueue(error);
        }

        internal AcpClient Client { get; }
        internal ConcurrentQueue<PermissionRequestEventArgs> Requests { get; } = new();
        internal ConcurrentQueue<JsonElement> Attempts { get; } = new();
        internal ConcurrentQueue<JsonElement> Responses { get; } = new();
        internal ConcurrentQueue<string> Errors { get; } = new();
        internal Task FirstResponseWritten => _firstResponseWritten.Task;
        internal Func<JsonElement, CancellationToken, Task<bool>>? ResponseSend { get; set; }
        public bool IsConnected { get; private set; }
        public event EventHandler<AcpTransportMessageReceivedEventArgs>? MessageReceived;
        public event EventHandler<AcpTransportErrorEventArgs>? ErrorOccurred { add { } remove { } }

        internal static async Task<PermissionPeer> CreateAsync()
        {
            var peer = new PermissionPeer();
            try
            {
                await peer.InitializeAsync();
                return peer;
            }
            catch
            {
                peer.Dispose();
                throw;
            }
        }

        internal async Task InitializeAsync()
        {
            var parameters = new InitializeParams(new ClientInfo("permission-cancellation-peer", "1"), new ClientCapabilities())
            { ProtocolVersion = AcpProtocolVersion.V1 };
            await Client.InitializeAsync(parameters, TestToken).WaitAsync(WaitTimeout, TestToken);
        }

        internal EventHandler<AcpTransportMessageReceivedEventArgs>? CaptureReceiver() => MessageReceived;

        internal void Request()
            => Deliver("""{"jsonrpc":"2.0","id":81,"method":"session/request_permission","params":{"sessionId":"session","toolCall":{"toolCallId":"call","title":"Run tests"},"options":[{"optionId":"allow","name":"Allow once","kind":"allow_once"}]}}""");

        internal void CancelRequest(string requestId = "81")
            => Deliver("{\"jsonrpc\":\"2.0\",\"method\":\"$/cancel_request\",\"params\":{\"requestId\":" + requestId + "}}");

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

        public async Task<bool> SendMessageAsync(string message, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var document = JsonDocument.Parse(message);
            var root = document.RootElement;
            if (root.TryGetProperty("method", out var method))
            {
                if (method.GetString() == "initialize")
                {
                    var initializationResponse = JsonSerializer.Serialize(
                        new InitializeResponse(AcpProtocolVersion.V1, new AgentInfo("permission-peer", "1"), new AgentCapabilities()),
                        AcpWireFormat.For(AcpProtocolVersion.V1).TypeInfo<InitializeResponse>());
                    Deliver("{\"jsonrpc\":\"2.0\",\"id\":" + root.GetProperty("id").GetRawText()
                        + ",\"result\":" + initializationResponse + "}");
                }
                return true;
            }

            var response = root.Clone();
            Attempts.Enqueue(response);
            if (ResponseSend is { } send && !await send(response, cancellationToken).ConfigureAwait(false))
            {
                return false;
            }
            cancellationToken.ThrowIfCancellationRequested();
            Responses.Enqueue(response);
            _firstResponseWritten.TrySetResult(true);
            return true;
        }

        private void Deliver(string message) => MessageReceived?.Invoke(this, new AcpTransportMessageReceivedEventArgs(message));

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Client.Dispose();
            IsConnected = false;
        }
    }
}
