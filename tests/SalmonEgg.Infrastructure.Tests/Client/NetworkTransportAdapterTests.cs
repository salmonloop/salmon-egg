using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using SalmonEgg.Infrastructure.Client;
using SalmonEgg.Infrastructure.Network;
using Xunit;
using TransportErrorEventArgs = SalmonEgg.Domain.Interfaces.Transport.TransportErrorEventArgs;
using TransportErrorKind = SalmonEgg.Domain.Interfaces.Transport.TransportErrorKind;
using TransportSendOptions = SalmonEgg.Domain.Interfaces.Transport.TransportSendOptions;

namespace SalmonEgg.Infrastructure.Tests.Client;

public sealed class NetworkTransportAdapterTests
{
    [Fact]
    public void MessageReceived_Should_Raise_For_NonEmpty_Messages()
    {
        var messages = new Subject<string>();
        var states = new Subject<TransportState>();
        var inner = new Mock<ITransport>();
        inner.SetupGet(x => x.Messages).Returns(messages);
        inner.SetupGet(x => x.StateChanges).Returns(states);

        var adapter = new NetworkTransportAdapter(inner.Object, "wss://example.com");
        var received = string.Empty;
        adapter.MessageReceived += (_, args) => received = args.Message;

        messages.OnNext("hello");

        Assert.Equal("hello", received);
    }

    [Fact]
    public void MessageReceived_Should_Ignore_Empty_Messages()
    {
        var messages = new Subject<string>();
        var states = new Subject<TransportState>();
        var inner = new Mock<ITransport>();
        inner.SetupGet(x => x.Messages).Returns(messages);
        inner.SetupGet(x => x.StateChanges).Returns(states);

        var adapter = new NetworkTransportAdapter(inner.Object, "wss://example.com");
        var raised = false;
        adapter.MessageReceived += (_, _) => raised = true;

        messages.OnNext(string.Empty);

        Assert.False(raised);
    }

    [Fact]
    public void StateChanges_Should_Update_IsConnected()
    {
        var messages = new Subject<string>();
        var states = new Subject<TransportState>();
        var inner = new Mock<ITransport>();
        inner.SetupGet(x => x.Messages).Returns(messages);
        inner.SetupGet(x => x.StateChanges).Returns(states);

        var adapter = new NetworkTransportAdapter(inner.Object, "https://example.com/events");

        states.OnNext(TransportState.Connected);
        Assert.True(adapter.IsConnected);

        states.OnNext(TransportState.Disconnected);
        Assert.False(adapter.IsConnected);
    }

    [Fact]
    public async Task SendMessageAsync_Should_Return_False_When_Message_Empty()
    {
        var messages = new Subject<string>();
        var states = new Subject<TransportState>();
        var inner = new Mock<ITransport>();
        inner.SetupGet(x => x.Messages).Returns(messages);
        inner.SetupGet(x => x.StateChanges).Returns(states);

        var adapter = new NetworkTransportAdapter(inner.Object, "https://example.com/events");

        var result = await adapter.SendMessageAsync(" ", CancellationToken.None);

        Assert.False(result);
        inner.Verify(x => x.SendAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SendMessageAsync_WhenCallerCancelsDuringSend_PreservesCancellationWithoutReportingFailure()
    {
        using var messages = new Subject<string>();
        using var states = new Subject<TransportState>();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inner = new Mock<ITransport>();
        inner.SetupGet(x => x.Messages).Returns(messages);
        inner.SetupGet(x => x.StateChanges).Returns(states);
        inner.Setup(x => x.SendAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(async (string _, CancellationToken token) =>
            {
                started.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            });
        using var adapter = new NetworkTransportAdapter(inner.Object, "https://example.com/events");
        states.OnNext(TransportState.Connected);
        var errors = new List<TransportErrorEventArgs>();
        adapter.ErrorOccurred += (_, error) => errors.Add(error);
        using var cancellation = new CancellationTokenSource();

        var send = adapter.SendMessageAsync("{}", cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => send);
        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.Empty(errors);
        Assert.True(adapter.IsConnected);
    }

    [Fact]
    public async Task ConnectAsync_Should_Return_False_And_Raise_Error_On_Exception()
    {
        var messages = new Subject<string>();
        var states = new Subject<TransportState>();
        var inner = new Mock<ITransport>();
        inner.SetupGet(x => x.Messages).Returns(messages);
        inner.SetupGet(x => x.StateChanges).Returns(states);
        inner.Setup(x => x.ConnectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("fail"));

        var adapter = new NetworkTransportAdapter(inner.Object, "wss://example.com/socket");
        var errorRaised = false;
        adapter.ErrorOccurred += (_, _) => errorRaised = true;

        var result = await adapter.ConnectAsync(CancellationToken.None);

        Assert.False(result);
        Assert.True(errorRaised);
    }

    [Fact]
    public async Task SendMessageAsync_WhenFatalSendFault_ReportsSendFailedOnceAndDisconnects()
    {
        // A fatal send fault: the inner transport pushes TransportState.Error from inside the send
        // call (as WebSocketTransport does) and then throws. The adapter must flip IsConnected so the
        // ACP client faults its in-flight requests, and must report the break exactly once, tagged
        // SendFailed rather than the generic kind from the state subscription.
        var messages = new Subject<string>();
        var states = new Subject<TransportStateChange>();
        var inner = new Mock<ITransport>();
        inner.SetupGet(x => x.Messages).Returns(messages);
        inner.SetupGet(x => x.StateChanges).Returns(states.Select(static change => change.State));
        inner.Setup(x => x.SendAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string _, CancellationToken _) =>
            {
                var failure = new System.Net.WebSockets.WebSocketException("socket closed");
                states.OnNext(new(TransportState.Error, TransportStateChangeOrigin.SendFailure, failure));
                throw failure;
            });

        var adapter = new NetworkTransportAdapter(new StateAwareTransport(inner.Object, states), "wss://example.com/socket");
        states.OnNext(new(TransportState.Connected));
        var errors = new List<TransportErrorEventArgs>();
        adapter.ErrorOccurred += (_, args) => errors.Add(args);

        var result = await adapter.SendMessageAsync("{}", CancellationToken.None);

        Assert.False(result);
        Assert.False(adapter.IsConnected);
        var error = Assert.Single(errors);
        Assert.Equal(TransportErrorKind.SendFailed, error.Kind);
    }

    [Fact]
    public async Task SendMessageAsync_WhenAsyncTransportReportsBreakAfterAwaiting_ReportsSendFailedOnce()
    {
        // The same fatal send as above, from a transport that actually awaits before reporting the
        // break. The reentrant report has to be attributed to the send that caused it even though the
        // send resumes on another thread, or the break is reported twice: once as the generic kind by
        // the state subscription and once with the precise kind by the send catch.
        var messages = new Subject<string>();
        var states = new Subject<TransportStateChange>();
        var inner = new Mock<ITransport>();
        inner.SetupGet(x => x.Messages).Returns(messages);
        inner.SetupGet(x => x.StateChanges).Returns(states.Select(static change => change.State));
        inner.Setup(x => x.SendAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(async (string _, CancellationToken _) =>
            {
                await Task.Yield();
                var failure = new System.Net.WebSockets.WebSocketException("socket closed");
                states.OnNext(new(TransportState.Error, TransportStateChangeOrigin.SendFailure, failure));
                throw failure;
            });

        var adapter = new NetworkTransportAdapter(new StateAwareTransport(inner.Object, states), "wss://example.com/socket");
        states.OnNext(new(TransportState.Connected));
        var errors = new List<TransportErrorEventArgs>();
        adapter.ErrorOccurred += (_, args) => errors.Add(args);

        var result = await adapter.SendMessageAsync("{}", CancellationToken.None);

        Assert.False(result);
        Assert.False(adapter.IsConnected);
        var error = Assert.Single(errors);
        Assert.Equal(TransportErrorKind.SendFailed, error.Kind);
    }

    [Fact]
    public async Task SendMessageAsync_WhenAnotherSendCompletesMidFlight_StillReportsTheBreakOnce()
    {
        // Two sends overlap: one is suspended when the other finishes. Whether a break belongs to the
        // suspended send cannot be tracked in state shared between sends, because the send that
        // finishes first would clear the other's claim and the suspended send's own break would then
        // also be reported as a generic transport error.
        var messages = new Subject<string>();
        var states = new Subject<TransportStateChange>();
        var suspended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inner = new Mock<ITransport>();
        inner.SetupGet(x => x.Messages).Returns(messages);
        inner.SetupGet(x => x.StateChanges).Returns(states.Select(static change => change.State));
        inner.Setup(x => x.SendAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(async (string message, CancellationToken _) =>
            {
                if (message != "slow")
                {
                    return;
                }

                await suspended.Task.ConfigureAwait(false);
                var failure = new System.Net.WebSockets.WebSocketException("socket closed");
                states.OnNext(new(TransportState.Error, TransportStateChangeOrigin.SendFailure, failure));
                throw failure;
            });

        var adapter = new NetworkTransportAdapter(new StateAwareTransport(inner.Object, states), "wss://example.com/socket");
        states.OnNext(new(TransportState.Connected));
        var errors = new List<TransportErrorEventArgs>();
        adapter.ErrorOccurred += (_, args) => errors.Add(args);

        var slowSend = adapter.SendMessageAsync("slow", CancellationToken.None);
        Assert.True(await adapter.SendMessageAsync("fast", CancellationToken.None));

        suspended.SetResult();
        Assert.False(await slowSend);

        var error = Assert.Single(errors);
        Assert.Equal(TransportErrorKind.SendFailed, error.Kind);
    }

    [Fact]
    public async Task SendMessageAsync_WhenTransientSendFault_ReportsSendFailedAndStaysConnected()
    {
        // A transient send fault leaves the connection usable: no Error state is pushed, so
        // IsConnected must stay true and the ACP client must not tear down in-flight requests.
        var messages = new Subject<string>();
        var states = new Subject<TransportState>();
        var inner = new Mock<ITransport>();
        inner.SetupGet(x => x.Messages).Returns(messages);
        inner.SetupGet(x => x.StateChanges).Returns(states);
        inner.Setup(x => x.SendAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("send buffer busy"));

        var adapter = new NetworkTransportAdapter(inner.Object, "wss://example.com/socket");
        states.OnNext(TransportState.Connected);
        var errors = new List<TransportErrorEventArgs>();
        adapter.ErrorOccurred += (_, args) => errors.Add(args);

        var result = await adapter.SendMessageAsync("{}", CancellationToken.None);

        Assert.False(result);
        Assert.True(adapter.IsConnected);
        var error = Assert.Single(errors);
        Assert.Equal(TransportErrorKind.SendFailed, error.Kind);
    }

    [Fact]
    public async Task SendMessageAsync_WhenNotConnected_ReportsNotConnected()
    {
        // Sending with no connection established is a different fault from a send that broke a live
        // connection, and is reported as such so downstream can tell them apart.
        var messages = new Subject<string>();
        var states = new Subject<TransportState>();
        var inner = new Mock<ITransport>();
        inner.SetupGet(x => x.Messages).Returns(messages);
        inner.SetupGet(x => x.StateChanges).Returns(states);
        inner.Setup(x => x.SendAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("WebSocket is not connected."));

        var adapter = new NetworkTransportAdapter(inner.Object, "wss://example.com/socket");
        var errors = new List<TransportErrorEventArgs>();
        adapter.ErrorOccurred += (_, args) => errors.Add(args);

        var result = await adapter.SendMessageAsync("{}", CancellationToken.None);

        Assert.False(result);
        var error = Assert.Single(errors);
        Assert.Equal(TransportErrorKind.NotConnected, error.Kind);
    }

    [Fact]
    public void StateChanges_Error_OutsideSend_ReportsGeneral()
    {
        // A break the transport reports on its own (a server-side close, not a send) still surfaces,
        // so the ACP client faults in-flight requests rather than waiting for a timeout.
        var messages = new Subject<string>();
        var states = new Subject<TransportState>();
        var inner = new Mock<ITransport>();
        inner.SetupGet(x => x.Messages).Returns(messages);
        inner.SetupGet(x => x.StateChanges).Returns(states);

        var adapter = new NetworkTransportAdapter(inner.Object, "wss://example.com/socket");
        states.OnNext(TransportState.Connected);
        var errors = new List<TransportErrorEventArgs>();
        adapter.ErrorOccurred += (_, args) => errors.Add(args);

        states.OnNext(TransportState.Error);

        Assert.False(adapter.IsConnected);
        var error = Assert.Single(errors);
        Assert.Equal(TransportErrorKind.General, error.Kind);
    }

    [Fact]
    public async Task DiagnosticOnlySend_WhenTransientFailure_ReturnsFalseWithoutErrorEvent()
    {
        using var messages = new Subject<string>();
        using var states = new Subject<TransportState>();
        var inner = new Mock<ITransport>();
        inner.SetupGet(x => x.Messages).Returns(messages);
        inner.SetupGet(x => x.StateChanges).Returns(states);
        inner.Setup(x => x.SendAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("send buffer busy"));
        using var adapter = new NetworkTransportAdapter(inner.Object, "https://example.com/acp");
        states.OnNext(TransportState.Connected);
        var errors = new List<TransportErrorEventArgs>();
        adapter.ErrorOccurred += (_, error) => errors.Add(error);

        Assert.False(await adapter.SendMessageAsync("{}", TransportSendOptions.DiagnosticOnly,
            TestContext.Current.CancellationToken));

        Assert.Empty(errors);
        Assert.True(adapter.IsConnected);
    }

    [Fact]
    public async Task DiagnosticOnlySend_WhenAnotherSendFails_ReportsOnlyTheOtherSend()
    {
        using var messages = new Subject<string>();
        using var states = new Subject<TransportState>();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inner = new Mock<ITransport>();
        inner.SetupGet(x => x.Messages).Returns(messages);
        inner.SetupGet(x => x.StateChanges).Returns(states);
        inner.Setup(x => x.SendAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(async (string message, CancellationToken token) =>
            {
                if (message == "cancel")
                {
                    await release.Task.WaitAsync(token);
                }
                throw new InvalidOperationException(message);
            });
        using var adapter = new NetworkTransportAdapter(inner.Object, "https://example.com/acp");
        states.OnNext(TransportState.Connected);
        var errors = new List<TransportErrorEventArgs>();
        adapter.ErrorOccurred += (_, error) => errors.Add(error);

        var cancel = adapter.SendMessageAsync("cancel", TransportSendOptions.DiagnosticOnly,
            TestContext.Current.CancellationToken);
        Assert.False(await adapter.SendMessageAsync("ordinary", TestContext.Current.CancellationToken));
        release.TrySetResult();
        Assert.False(await cancel);

        Assert.Contains("ordinary", Assert.Single(errors).ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DiagnosticOnlySend_WhenChildReaderFails_DoesNotHideInheritedExecutionContextError()
    {
        using var messages = new Subject<string>();
        using var states = new Subject<TransportState>();
        var readerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseReader = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSend = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? reader = null;
        var inner = new Mock<ITransport>();
        inner.SetupGet(x => x.Messages).Returns(messages);
        inner.SetupGet(x => x.StateChanges).Returns(states);
        inner.Setup(x => x.SendAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(async (string _, CancellationToken token) =>
            {
                // Matches HTTP initialize starting its long-lived SSE reader inside this send.
                reader = Task.Run(async () =>
                {
                    readerStarted.TrySetResult();
                    await releaseReader.Task.WaitAsync(token);
                    states.OnNext(TransportState.Error);
                }, token);
                await releaseSend.Task.WaitAsync(token);
            });
        using var adapter = new NetworkTransportAdapter(inner.Object, "https://example.com/acp");
        states.OnNext(TransportState.Connected);
        var errors = new List<TransportErrorEventArgs>();
        adapter.ErrorOccurred += (_, error) => errors.Add(error);

        var send = adapter.SendMessageAsync("{}", TransportSendOptions.DiagnosticOnly,
            TestContext.Current.CancellationToken);
        await readerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        releaseReader.TrySetResult();
        await reader!;
        releaseSend.TrySetResult();
        await send;

        Assert.Equal(TransportErrorKind.General, Assert.Single(errors).Kind);
        Assert.False(adapter.IsConnected);
    }

    [Fact]
    public async Task DiagnosticOnlySend_WhenFatalSendFails_StillReportsDisconnect()
    {
        using var messages = new Subject<string>();
        using var states = new Subject<TransportStateChange>();
        var inner = new Mock<ITransport>();
        inner.SetupGet(x => x.Messages).Returns(messages);
        inner.SetupGet(x => x.StateChanges).Returns(states.Select(static change => change.State));
        inner.Setup(x => x.SendAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string _, CancellationToken _) =>
            {
                var failure = new System.Net.WebSockets.WebSocketException("closed socket");
                states.OnNext(new(TransportState.Error, TransportStateChangeOrigin.SendFailure, failure));
                throw failure;
            });
        using var adapter = new NetworkTransportAdapter(new StateAwareTransport(inner.Object, states), "wss://example.com/acp");
        states.OnNext(new(TransportState.Connected));
        var errors = new List<TransportErrorEventArgs>();
        adapter.ErrorOccurred += (_, error) => errors.Add(error);

        Assert.False(await adapter.SendMessageAsync("{}", TransportSendOptions.DiagnosticOnly,
            TestContext.Current.CancellationToken));

        Assert.Equal(TransportErrorKind.SendFailed, Assert.Single(errors).Kind);
        Assert.False(adapter.IsConnected);
    }

    [Fact]
    public async Task DisconnectAsync_OnException_ReportsDisconnectFailed()
    {
        var messages = new Subject<string>();
        var states = new Subject<TransportState>();
        var inner = new Mock<ITransport>();
        inner.SetupGet(x => x.Messages).Returns(messages);
        inner.SetupGet(x => x.StateChanges).Returns(states);
        inner.Setup(x => x.DisconnectAsync()).ThrowsAsync(new InvalidOperationException("fail"));

        var adapter = new NetworkTransportAdapter(inner.Object, "wss://example.com/socket");
        var errors = new List<TransportErrorEventArgs>();
        adapter.ErrorOccurred += (_, args) => errors.Add(args);

        var result = await adapter.DisconnectAsync();

        Assert.False(result);
        var error = Assert.Single(errors);
        Assert.Equal(TransportErrorKind.DisconnectFailed, error.Kind);
    }

    private sealed class StateAwareTransport(ITransport inner, IObservable<TransportStateChange> stateTransitions)
        : ITransport, ITransportStateSource
    {
        public IObservable<TransportStateChange> StateTransitions => stateTransitions;
        public IObservable<TransportState> StateChanges => inner.StateChanges;
        public IObservable<string> Messages => inner.Messages;
        public Task ConnectAsync(string url, CancellationToken ct) => inner.ConnectAsync(url, ct);
        public Task DisconnectAsync() => inner.DisconnectAsync();
        public Task SendAsync(string message, CancellationToken ct) => inner.SendAsync(message, ct);
    }
}
