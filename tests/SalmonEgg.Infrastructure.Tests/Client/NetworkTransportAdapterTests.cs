using System;
using System.Collections.Generic;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using SalmonEgg.Infrastructure.Client;
using SalmonEgg.Infrastructure.Network;
using Xunit;
using TransportErrorEventArgs = SalmonEgg.Domain.Interfaces.Transport.TransportErrorEventArgs;
using TransportErrorKind = SalmonEgg.Domain.Interfaces.Transport.TransportErrorKind;

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
        var states = new Subject<TransportState>();
        var inner = new Mock<ITransport>();
        inner.SetupGet(x => x.Messages).Returns(messages);
        inner.SetupGet(x => x.StateChanges).Returns(states);
        inner.Setup(x => x.SendAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string _, CancellationToken _) =>
            {
                states.OnNext(TransportState.Error);
                throw new System.Net.WebSockets.WebSocketException("socket closed");
            });

        var adapter = new NetworkTransportAdapter(inner.Object, "wss://example.com/socket");
        states.OnNext(TransportState.Connected);
        var errors = new List<TransportErrorEventArgs>();
        adapter.ErrorOccurred += (_, args) => errors.Add(args);

        var result = await adapter.SendMessageAsync("{}", CancellationToken.None);

        Assert.False(result);
        Assert.False(adapter.IsConnected);
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
}
