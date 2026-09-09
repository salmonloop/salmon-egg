using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using SalmonEgg.Acp.Client;
using SalmonEgg.Acp.JsonRpc;
using SalmonEgg.Acp.Mcp;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Acp.Serialization;
using Xunit;

namespace SalmonEgg.Acp.Tests.Client;

/// <summary>
/// Outbound request cancellation. ACP puts <c>$/cancel_request</c> on the side that issued the
/// request: abandoning a local await leaves the peer running, so anything with side effects (a
/// terminal, a file write) keeps going after the user has cancelled.
/// </summary>
/// <remarks>
/// These assert the wire payload, not just local state — a client that merely stops waiting looks
/// identical from the outside, which is exactly the defect.
/// </remarks>
public sealed class AcpClientRequestCancellationTests
{
    private static readonly string AbsoluteCwd = Path.GetFullPath(Path.Combine(
        Path.GetTempPath(),
        "salmon-egg-tests",
        "cancel-request"));

    private readonly Mock<IAcpTransport> _transportMock = new();
    private readonly Mock<IAcpClientLogger> _loggerMock = new();
    private readonly MessageParser _parser = new();
    private readonly ConcurrentQueue<string> _sent = new();

    public AcpClientRequestCancellationTests()
    {
        _transportMock.SetupGet(t => t.IsConnected).Returns(true);
        _transportMock.Setup(t => t.DisconnectAsync()).ReturnsAsync(true);
        _transportMock
            .Setup(t => t.SendMessageAsync(It.IsAny<string>(), It.IsAny<AcpTransportSendOptions>(), It.IsAny<CancellationToken>()))
            .Returns<string, AcpTransportSendOptions, CancellationToken>((message, _, token) =>
                _transportMock.Object.SendMessageAsync(message, token));
        _transportMock
            .Setup(t => t.SendMessageAsync(It.IsRegex(@"cancel_request"), It.IsAny<CancellationToken>()))
            .Returns<string, CancellationToken>((message, _) =>
            {
                _sent.Enqueue(message);
                return Task.FromResult(true);
            });
    }

    [Fact]
    public async Task DispatchedRequest_WhenCallerCancels_SendsCancelRequestCarryingTheOriginalId()
    {
        using var client = await CreateInitializedClientAsync();
        SetupSilentSend("session/new");
        using var cancellation = new CancellationTokenSource();

        var pending = client.CreateSessionAsync(new SessionNewParams(AbsoluteCwd, null), cancellation.Token);
        await WaitForSentMethodAsync("session/new");
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);

        var request = Assert.Single(SentRequests("session/new"));
        var cancel = await WaitForSentNotificationAsync(CancelRequestParams.Method);
        Assert.Equal(
            request.RootElement.GetProperty("id").GetRawText(),
            cancel.RootElement.GetProperty("params").GetProperty("requestId").GetRawText());
    }

    [Fact]
    public async Task CancelRequest_IsAProtocolLevelNotificationWithNoId()
    {
        using var client = await CreateInitializedClientAsync();
        SetupSilentSend("session/new");
        using var cancellation = new CancellationTokenSource();

        var pending = client.CreateSessionAsync(new SessionNewParams(AbsoluteCwd, null), cancellation.Token);
        await WaitForSentMethodAsync("session/new");
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);

        var cancel = await WaitForSentNotificationAsync(CancelRequestParams.Method);

        // A notification, not a request: carrying an id would make the peer owe us a response.
        Assert.Equal("2.0", cancel.RootElement.GetProperty("jsonrpc").GetString());
        Assert.False(cancel.RootElement.TryGetProperty("id", out _));
        Assert.Equal("$/cancel_request", cancel.RootElement.GetProperty("method").GetString());
    }

    [Fact]
    public async Task CancelRequest_IsSentForEveryRequestMethod_NotOnlyPrompts()
    {
        // session/cancel already covered the prompt turn. The gap was every other outbound request,
        // so the behaviour has to live on the shared send path rather than one method.
        using var client = await CreateInitializedClientAsync(
            new AgentCapabilities(loadSession: true));
        SetupSilentSend("session/load");
        using var cancellation = new CancellationTokenSource();

        var pending = client.LoadSessionAsync(
            new SessionLoadParams("session-1", AbsoluteCwd, new List<McpServer>()),
            cancellation.Token);
        await WaitForSentMethodAsync("session/load");
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);

        var request = Assert.Single(SentRequests("session/load"));
        var cancel = await WaitForSentNotificationAsync(CancelRequestParams.Method);
        Assert.Equal(
            request.RootElement.GetProperty("id").GetRawText(),
            cancel.RootElement.GetProperty("params").GetProperty("requestId").GetRawText());
    }

    [Fact]
    public async Task RequestWriteInProgress_WhenCallerCancels_SendsCancelRequestBecauseThePeerMayAlreadyHaveTheFrame()
    {
        using var client = await CreateInitializedClientAsync();
        var writeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _transportMock
            .Setup(t => t.SendMessageAsync(It.IsRegex("session/new"), It.IsAny<CancellationToken>()))
            .Returns<string, CancellationToken>(async (message, token) =>
            {
                _sent.Enqueue(message); // Represents bytes accepted by the real pipe/socket.
                writeStarted.TrySetResult();
                await releaseWrite.Task.ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                return true;
            });
        using var cancellation = new CancellationTokenSource();

        var pending = client.CreateSessionAsync(new SessionNewParams(AbsoluteCwd, null), cancellation.Token);
        await writeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();
        releaseWrite.TrySetResult();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        await WaitForSentNotificationAsync(CancelRequestParams.Method);
    }

    [Fact]
    public async Task UndispatchedRequest_WhenCancelledBeforeTheSendSucceeds_SendsNoCancelRequest()
    {
        // Nothing reached the peer, so there is no request for it to cancel.
        using var client = await CreateInitializedClientAsync();
        _transportMock
            .Setup(t => t.SendMessageAsync(It.IsRegex("session/new"), It.IsAny<CancellationToken>()))
            .Returns<string, CancellationToken>((message, token) =>
            {
                _sent.Enqueue(message);
                return Task.FromCanceled<bool>(new CancellationToken(canceled: true));
            });
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.CreateSessionAsync(new SessionNewParams(AbsoluteCwd, null), cancellation.Token));

        Assert.Empty(SentNotifications(CancelRequestParams.Method));
    }

    [Fact]
    public async Task DisconnectedTransport_WhenCallerCancels_SendsNoCancelRequest()
    {
        using var client = await CreateInitializedClientAsync();
        SetupSilentSend("session/new");
        using var cancellation = new CancellationTokenSource();

        var pending = client.CreateSessionAsync(new SessionNewParams(AbsoluteCwd, null), cancellation.Token);
        await WaitForSentMethodAsync("session/new");

        // The connection dropped while the request was in flight; a cancellation notification has
        // nowhere to go and must not become a second error surface.
        _transportMock.SetupGet(t => t.IsConnected).Returns(false);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);

        Assert.Empty(SentNotifications(CancelRequestParams.Method));
    }

    [Fact]
    public async Task FailedCancelRequestSend_RequestsDiagnosticOnlyFailureOwnership()
    {
        using var client = await CreateInitializedClientAsync();
        var errors = new ConcurrentQueue<string>();
        client.ErrorOccurred += (_, error) => errors.Enqueue(error);
        SetupSilentSend("session/new");
        _transportMock
            .Setup(t => t.SendMessageAsync(It.IsRegex(@"cancel_request"), It.IsAny<AcpTransportSendOptions>(), It.IsAny<CancellationToken>()))
            .Returns<string, AcpTransportSendOptions, CancellationToken>((_, options, _) =>
            {
                if (options != AcpTransportSendOptions.DiagnosticOnly)
                {
                    _transportMock.Raise(t => t.ErrorOccurred += null,
                        new AcpTransportErrorEventArgs("Failed to send message", new IOException("send failed"),
                            AcpTransportErrorKind.SendFailed));
                }
                return Task.FromResult(false);
            });
        using var cancellation = new CancellationTokenSource();

        var pending = client.CreateSessionAsync(new SessionNewParams(AbsoluteCwd, null), cancellation.Token);
        await WaitForSentMethodAsync("session/new");
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);

        Assert.Empty(errors);
        _transportMock.Verify(t => t.SendMessageAsync(It.IsRegex(@"cancel_request"),
            AcpTransportSendOptions.DiagnosticOnly, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FailedCancelRequestSend_DoesNotSurfaceAsAClientError()
    {
        // '$/' notifications are explicitly ignorable, so failing to deliver one is not a fault the
        // user can act on — and the failure surface holds such a message until the next success.
        using var client = await CreateInitializedClientAsync();
        var errors = new List<string>();
        client.ErrorOccurred += (_, error) =>
        {
            lock (errors)
            {
                errors.Add(error);
            }
        };

        SetupSilentSend("session/new");
        _transportMock
            .Setup(t => t.SendMessageAsync(It.IsRegex(@"cancel_request"), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("socket closed"));

        using var cancellation = new CancellationTokenSource();
        var pending = client.CreateSessionAsync(new SessionNewParams(AbsoluteCwd, null), cancellation.Token);
        await WaitForSentMethodAsync("session/new");
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);

        string[] surfaced;
        lock (errors)
        {
            surfaced = [.. errors];
        }

        Assert.Empty(surfaced);
        _loggerMock.Verify(
            logger => logger.Log(
                AcpClientLogLevel.Warning,
                "CANCEL_REQUEST_SEND_FAILED",
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<Exception?>()),
            Times.Once);
    }

    [Fact]
    public async Task ResponseReceivedBeforeSendObservesCancellation_WinsWithoutSendingCancelRequest()
    {
        using var client = await CreateInitializedClientAsync();
        using var cancellation = new CancellationTokenSource();
        _transportMock
            .Setup(t => t.SendMessageAsync(It.IsRegex("session/new"), It.IsAny<CancellationToken>()))
            .Returns<string, CancellationToken>((message, token) =>
            {
                _sent.Enqueue(message);
                using var request = JsonDocument.Parse(message);
                RaiseTransportMessage("{\"jsonrpc\":\"2.0\",\"id\":" + request.RootElement.GetProperty("id").GetRawText()
                    + ",\"result\":{\"sessionId\":\"completed-before-cancel\"}}");
                cancellation.Cancel();
                return Task.FromCanceled<bool>(token);
            });

        var response = await client.CreateSessionAsync(new SessionNewParams(AbsoluteCwd, null), cancellation.Token);

        Assert.Equal("completed-before-cancel", response.SessionId);
        Assert.Empty(SentNotifications(CancelRequestParams.Method));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RequestFromPreviousConnection_WhenSendObservesCancellationAfterReconnect_DoesNotCancelOnNewConnection(bool fatalDisconnect)
    {
        using var client = await CreateInitializedClientAsync();
        var writeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _transportMock
            .Setup(t => t.SendMessageAsync(It.IsRegex("session/new"), It.IsAny<CancellationToken>()))
            .Returns<string, CancellationToken>(async (message, token) =>
            {
                _sent.Enqueue(message);
                writeStarted.TrySetResult();
                await releaseWrite.Task.ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                return true;
            });
        using var previousCancellation = new CancellationTokenSource();
        var previous = client.CreateSessionAsync(new SessionNewParams(AbsoluteCwd, null), previousCancellation.Token);
        await writeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        try
        {
            if (fatalDisconnect)
            {
                _transportMock.SetupGet(t => t.IsConnected).Returns(false);
                _transportMock.Raise(t => t.ErrorOccurred += null,
                    new AcpTransportErrorEventArgs("old reader disconnected", kind: AcpTransportErrorKind.StdoutReadFailed));
            }
            else
            {
                await client.DisconnectAsync();
            }
            _transportMock.SetupGet(t => t.IsConnected).Returns(true);
            await client.InitializeAsync(new InitializeParams(new ClientInfo("reconnected", "1.0"), new ClientCapabilities()),
                TestContext.Current.CancellationToken);
            await previousCancellation.CancelAsync();
            releaseWrite.TrySetResult();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => previous);
            Assert.Empty(SentNotifications(CancelRequestParams.Method));
            Assert.True(client.IsInitialized);

            SetupSilentSend("session/new");
            using var currentCancellation = new CancellationTokenSource();
            var current = client.CreateSessionAsync(new SessionNewParams(AbsoluteCwd, null), currentCancellation.Token);
            await currentCancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => current);
            var currentRequest = SentRequests("session/new").Last();
            var cancel = Assert.Single(SentNotifications(CancelRequestParams.Method));
            Assert.Equal(currentRequest.RootElement.GetProperty("id").GetRawText(),
                cancel.RootElement.GetProperty("params").GetProperty("requestId").GetRawText());
        }
        finally
        {
            releaseWrite.TrySetResult();
        }
    }

    [Fact]
    public async Task CancellationNotification_WhenTransportWaits_ExpiresItsTokenAndCompletesWithoutError()
    {
        using var client = await CreateInitializedClientAsync();
        SetupSilentSend("session/new");
        var errors = new ConcurrentQueue<string>();
        client.ErrorOccurred += (_, error) => errors.Enqueue(error);
        var terminated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _transportMock
            .Setup(t => t.SendMessageAsync(It.IsRegex("cancel_request"), It.IsAny<AcpTransportSendOptions>(), It.IsAny<CancellationToken>()))
            .Returns<string, AcpTransportSendOptions, CancellationToken>(async (message, _, token) =>
            {
                _sent.Enqueue(message);
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                    return true;
                }
                finally
                {
                    terminated.TrySetResult();
                }
            });
        using var cancellation = new CancellationTokenSource();
        var pending = client.CreateSessionAsync(new SessionNewParams(AbsoluteCwd, null), cancellation.Token);
        await WaitForSentMethodAsync("session/new");
        await cancellation.CancelAsync();

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(
            AcpClient.CancellationNotificationTimeout + TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
        await terminated.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.Empty(errors);
        Assert.Single(SentNotifications(CancelRequestParams.Method));
    }

    [Fact]
    public async Task InitializeWhileAnotherHandshakeIsPending_RejectsDuplicateWithoutReplacingItsOwner()
    {
        using var client = new AcpClient(_transportMock.Object, _loggerMock.Object);
        SetupSilentSend("initialize");
        var parameters = new InitializeParams(new ClientInfo("handshake", "1.0"), new ClientCapabilities());
        var pending = client.InitializeAsync(parameters, TestContext.Current.CancellationToken);
        await WaitForSentMethodAsync("initialize");

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.InitializeAsync(parameters,
            TestContext.Current.CancellationToken));
        var request = Assert.Single(SentRequests("initialize"));
        RaiseTransportMessage("{\"jsonrpc\":\"2.0\",\"id\":" + request.RootElement.GetProperty("id").GetRawText()
            + ",\"result\":{\"protocolVersion\":1,\"agentCapabilities\":{}}}");
        await pending;
        Assert.True(client.IsInitialized);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedInitialize_ReleasesConnectionOwnerSoAnotherHandshakeCanSucceed(bool callerCancels)
    {
        using var client = new AcpClient(_transportMock.Object, _loggerMock.Object);
        using var cancellation = new CancellationTokenSource();
        SetupSilentSend("initialize");
        var parameters = new InitializeParams(new ClientInfo("handshake", "1.0"), new ClientCapabilities());
        var pending = client.InitializeAsync(parameters, cancellation.Token);
        await WaitForSentMethodAsync("initialize");
        var request = Assert.Single(SentRequests("initialize"));
        if (callerCancels)
        {
            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        }
        else
        {
            RaiseTransportMessage("{\"jsonrpc\":\"2.0\",\"id\":" + request.RootElement.GetProperty("id").GetRawText()
                + ",\"error\":{\"code\":-32603,\"message\":\"handshake rejected\"}}");
            await Assert.ThrowsAsync<AcpException>(() => pending);
        }

        Assert.False(client.IsInitialized);
        SetupInitializeResponse();
        await client.InitializeAsync(parameters, TestContext.Current.CancellationToken);
        RaiseTransportMessage("{\"jsonrpc\":\"2.0\",\"id\":" + request.RootElement.GetProperty("id").GetRawText()
            + ",\"error\":{\"code\":-32800,\"message\":\"Cancelled\"}}");
        _loggerMock.Verify(logger => logger.Log(It.IsAny<AcpClientLogLevel>(), "CANCELLED_REQUEST_SETTLED",
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<Exception?>()), Times.Never);
        Assert.True(client.IsInitialized);
    }

    [Fact]
    public async Task PreviousInitializeCompletesAfterDisconnectAndNewHandshake_DoesNotReplaceTheNewConnection()
    {
        using var client = new AcpClient(_transportMock.Object, _loggerMock.Object);
        var releaseOldWrite = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _transportMock
            .Setup(t => t.SendMessageAsync(It.IsRegex("initialize"), It.IsAny<CancellationToken>()))
            .Returns<string, CancellationToken>((message, _) =>
            {
                _sent.Enqueue(message);
                using var request = JsonDocument.Parse(message);
                RaiseTransportMessage("{\"jsonrpc\":\"2.0\",\"id\":" + request.RootElement.GetProperty("id").GetRawText()
                    + ",\"result\":{\"protocolVersion\":1,\"agentInfo\":{\"name\":\"old-agent\",\"version\":\"1.0\"},\"agentCapabilities\":{}}}");
                return releaseOldWrite.Task;
            });
        var initialized = 0;
        client.Initialized += (_, _) => initialized++;
        var parameters = new InitializeParams(new ClientInfo("handshake", "1.0"), new ClientCapabilities());
        var old = client.InitializeAsync(parameters, TestContext.Current.CancellationToken);
        await WaitForSentMethodAsync("initialize");
        try
        {
            await client.DisconnectAsync();
            SetupInitializeResponse();
            await client.InitializeAsync(parameters, TestContext.Current.CancellationToken);
            releaseOldWrite.TrySetResult(true);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => old);
            Assert.True(client.IsInitialized);
            Assert.Equal("TestAgent", client.AgentInfo!.Name);
            Assert.Equal(1, initialized);
        }
        finally
        {
            releaseOldWrite.TrySetResult(true);
        }
    }

    [Fact]
    public async Task CancellationNotification_WhenLegacyTransportIgnoresToken_CallerStillFinishesAndLateFaultIsObserved()
    {
        using var client = await CreateInitializedClientAsync();
        SetupSilentSend("session/new");
        var lateSend = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var errors = new ConcurrentQueue<string>();
        client.ErrorOccurred += (_, error) => errors.Enqueue(error);
        _transportMock
            .Setup(t => t.SendMessageAsync(It.IsRegex("cancel_request"), It.IsAny<AcpTransportSendOptions>(), It.IsAny<CancellationToken>()))
            .Returns(lateSend.Task);
        using var cancellation = new CancellationTokenSource();
        var pending = client.CreateSessionAsync(new SessionNewParams(AbsoluteCwd, null), cancellation.Token);
        await WaitForSentMethodAsync("session/new");
        await cancellation.CancelAsync();

        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(
                AcpClient.CancellationNotificationTimeout + TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken));
            Assert.Empty(errors);
            // Disposal must not wait for a legacy transport's uncooperative pending send either.
            client.Dispose();
        }
        finally
        {
            // Even the deliberately non-cooperative fixture is completed before the test exits.
            lateSend.TrySetException(new IOException("late best-effort send failure"));
        }
    }

    [Fact]
    public async Task TwoRequestsCancelledIndependently_EachRetainsItsOwnCorrelationUntilPeerSettlement()
    {
        using var client = await CreateInitializedClientAsync();
        SetupSilentSend("session/new");
        using var firstCancellation = new CancellationTokenSource();
        using var secondCancellation = new CancellationTokenSource();
        var first = client.CreateSessionAsync(new SessionNewParams(AbsoluteCwd, null), firstCancellation.Token);
        var second = client.CreateSessionAsync(new SessionNewParams(AbsoluteCwd, null), secondCancellation.Token);
        await WaitAsync(() => SentRequests("session/new").Length == 2, "both requests");

        await firstCancellation.CancelAsync();
        await secondCancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);

        var requests = SentRequests("session/new").Select(document => document.RootElement.GetProperty("id").GetRawText()).ToArray();
        var notifications = SentNotifications(CancelRequestParams.Method);
        Assert.Equal(2, notifications.Length);
        Assert.Equal(requests.Order(), notifications.Select(document =>
            document.RootElement.GetProperty("params").GetProperty("requestId").GetRawText()).Order());
        foreach (var id in requests.Reverse())
        {
            RaiseTransportMessage("{\"jsonrpc\":\"2.0\",\"id\":" + id
                + ",\"error\":{\"code\":-32800,\"message\":\"Cancelled\"}}");
        }
        _loggerMock.Verify(logger => logger.Log(AcpClientLogLevel.Information, "CANCELLED_REQUEST_SETTLED",
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<Exception?>()), Times.Exactly(2));
    }

    [Fact]
    public async Task DisconnectDuringCancellationNotification_CancelsItsSendAndClearsOtherPendingRequests()
    {
        using var client = await CreateInitializedClientAsync();
        SetupSilentSend("session/new");
        var notificationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var notificationEnded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var errors = new ConcurrentQueue<string>();
        client.ErrorOccurred += (_, error) => errors.Enqueue(error);
        _transportMock
            .Setup(t => t.SendMessageAsync(It.IsRegex("cancel_request"), It.IsAny<AcpTransportSendOptions>(), It.IsAny<CancellationToken>()))
            .Returns<string, AcpTransportSendOptions, CancellationToken>(async (_, _, token) =>
            {
                notificationStarted.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                    return true;
                }
                finally
                {
                    notificationEnded.TrySetResult();
                }
            });
        using var cancellation = new CancellationTokenSource();
        var cancelled = client.CreateSessionAsync(new SessionNewParams(AbsoluteCwd, null), cancellation.Token);
        var other = client.CreateSessionAsync(new SessionNewParams(AbsoluteCwd, null), TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();
        await notificationStarted.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);

        _transportMock.SetupGet(t => t.IsConnected).Returns(false);
        _transportMock.Raise(t => t.ErrorOccurred += null,
            new AcpTransportErrorEventArgs("reader disconnected", kind: AcpTransportErrorKind.StdoutReadFailed));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => other);
        Assert.Contains("reader disconnected", failure.Message, StringComparison.Ordinal);
        await notificationEnded.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.False(client.IsInitialized);
        Assert.Equal("reader disconnected", Assert.Single(errors));
        foreach (var request in SentRequests("session/new"))
        {
            RaiseTransportMessage("{\"jsonrpc\":\"2.0\",\"id\":" + request.RootElement.GetProperty("id").GetRawText()
                + ",\"error\":{\"code\":-32800,\"message\":\"Cancelled\"}}");
        }
        _loggerMock.Verify(logger => logger.Log(It.IsAny<AcpClientLogLevel>(), "CANCELLED_REQUEST_SETTLED",
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<Exception?>()), Times.Never);
    }

    [Fact]
    public async Task CancelledErrorResponse_ArrivingAfterCancellation_IsAbsorbedWithoutAClientError()
    {
        // ACP requires the peer to send a terminal response for the cancelled request. Our pending
        // table has to be able to receive -32800 rather than have it land as an unmatched frame or
        // an error the user sees.
        using var client = await CreateInitializedClientAsync();
        var errors = new List<string>();
        client.ErrorOccurred += (_, error) =>
        {
            lock (errors)
            {
                errors.Add(error);
            }
        };

        SetupSilentSend("session/new");
        using var cancellation = new CancellationTokenSource();
        var pending = client.CreateSessionAsync(new SessionNewParams(AbsoluteCwd, null), cancellation.Token);
        await WaitForSentMethodAsync("session/new");
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        await WaitForSentNotificationAsync(CancelRequestParams.Method);

        var requestId = Assert.Single(SentRequests("session/new")).RootElement.GetProperty("id").GetRawText();
        RaiseTransportMessage(
            "{\"jsonrpc\":\"2.0\",\"id\":" + requestId + ",\"error\":{\"code\":-32800,\"message\":\"Request cancelled\"}}");

        await WaitForLoggedCodeAsync("CANCELLED_REQUEST_SETTLED");

        string[] surfaced;
        lock (errors)
        {
            surfaced = [.. errors];
        }

        Assert.Empty(surfaced);
    }

    [Fact]
    public async Task SessionCancel_QueuedWriteAcrossReconnect_NeverSendsToReplacementConnection()
    {
        // Arrange
        var sessionStore = new Mock<IAcpClientSessionStore>();
        sessionStore.Setup(store => store.CancelSessionAsync(It.IsAny<string>())).ReturnsAsync(true);
        using var client = await CreateInitializedClientAsync(sessionStore: sessionStore.Object);
        var writeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _transportMock.Setup(transport => transport.SendMessageAsync(It.IsRegex("session/cancel"), It.IsAny<CancellationToken>()))
            .Returns<string, CancellationToken>(async (message, token) =>
            {
                writeStarted.TrySetResult();
                await releaseWrite.Task.ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                _sent.Enqueue(message);
                return true;
            });
        var cancellation = client.CancelSessionAsync(new SessionCancelParams("session-1"), TestContext.Current.CancellationToken);

        try
        {
            await writeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            await ReconnectForSessionCancellationAsync(client);

            // Act
            releaseWrite.TrySetResult();
            var failure = await Record.ExceptionAsync(() => cancellation.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));

            // Assert
            Assert.Empty(SentNotifications("session/cancel"));
            Assert.IsAssignableFrom<OperationCanceledException>(failure);
            sessionStore.Verify(store => store.CancelSessionAsync(It.IsAny<string>()), Times.Never);

            SetupSilentSend("session/cancel");
            await client.CancelSessionAsync(new SessionCancelParams("session-1"), TestContext.Current.CancellationToken);
            Assert.Single(SentNotifications("session/cancel"));
            sessionStore.Verify(store => store.CancelSessionAsync("session-1"), Times.Once);
        }
        finally
        {
            releaseWrite.TrySetResult();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SessionCancel_QueuedWriteCancelledOrDisposed_NeverSends(bool dispose)
    {
        // Arrange
        var sessionStore = new Mock<IAcpClientSessionStore>();
        using var client = await CreateInitializedClientAsync(sessionStore: sessionStore.Object);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var writeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _transportMock.Setup(transport => transport.SendMessageAsync(It.IsRegex("session/cancel"), It.IsAny<CancellationToken>()))
            .Returns<string, CancellationToken>(async (message, token) =>
            {
                writeStarted.TrySetResult();
                await releaseWrite.Task.ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                _sent.Enqueue(message);
                return true;
            });
        var cancellation = client.CancelSessionAsync(new SessionCancelParams("session-1"), caller.Token);

        try
        {
            await writeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            // Act
            if (dispose) client.Dispose();
            else caller.Cancel();
            releaseWrite.TrySetResult();
            var failure = await Record.ExceptionAsync(() => cancellation.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));

            // Assert
            Assert.Empty(SentNotifications("session/cancel"));
            Assert.IsAssignableFrom<OperationCanceledException>(failure);
            sessionStore.Verify(store => store.CancelSessionAsync(It.IsAny<string>()), Times.Never);
        }
        finally
        {
            releaseWrite.TrySetResult();
        }
    }

    [Theory]
    [InlineData("session/request_permission")]
    [InlineData("fs/read_text_file")]
    public async Task SessionCancel_WriteReturnsAfterReconnect_LeavesReplacementRequestsAndStoreUntouched(string method)
    {
        // Arrange: the frame reached the old connection, but its write has not returned yet.
        var sessionStore = new Mock<IAcpClientSessionStore>();
        using var client = await CreateInitializedClientAsync(sessionStore: sessionStore.Object,
            clientCapabilities: new ClientCapabilities(fs: new FsCapability()));
        SetupResponseRecording();
        var writeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseWrite = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _transportMock.Setup(transport => transport.SendMessageAsync(It.IsRegex("session/cancel"), It.IsAny<CancellationToken>()))
            .Returns<string, CancellationToken>((message, _) =>
            {
                _sent.Enqueue(message);
                writeStarted.TrySetResult();
                return releaseWrite.Task;
            });
        var cancellation = client.CancelSessionAsync(new SessionCancelParams("session-1"), TestContext.Current.CancellationToken);

        try
        {
            await writeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            await ReconnectForSessionCancellationAsync(client);
            await RaisePendingSessionRequestAsync(client, method, 401);

            // Act
            releaseWrite.TrySetResult(true);
            var failure = await Record.ExceptionAsync(() => cancellation.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));

            // Assert
            Assert.Empty(_sent.Select(_parser.ParseMessage).OfType<JsonRpcResponse>());
            sessionStore.Verify(store => store.CancelSessionAsync(It.IsAny<string>()), Times.Never);
            Assert.IsAssignableFrom<OperationCanceledException>(failure);
            Assert.True(await CompleteSessionRequestAsync(client, method, 401));
            Assert.False(Assert.Single(_sent.Select(_parser.ParseMessage).OfType<JsonRpcResponse>()).IsError);
        }
        finally
        {
            releaseWrite.TrySetResult(true);
        }
    }

    [Theory]
    [InlineData("session/request_permission")]
    [InlineData("fs/read_text_file")]
    public async Task SessionCancel_DrainingResponseAcrossReconnect_NeverWritesOrCancelsReplacement(string method)
    {
        // Arrange: pause the response before its physical write while session cancellation drains callbacks.
        var sessionStore = new Mock<IAcpClientSessionStore>();
        using var client = await CreateInitializedClientAsync(sessionStore: sessionStore.Object,
            clientCapabilities: new ClientCapabilities(fs: new FsCapability()));
        SetupSilentSend("session/cancel");
        await RaisePendingSessionRequestAsync(client, method, 402);
        var writeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _transportMock.Setup(transport => transport.SendMessageAsync(
                It.Is<string>(message => IsResponseFrame(message)), It.IsAny<CancellationToken>()))
            .Returns<string, CancellationToken>(async (message, token) =>
            {
                writeStarted.TrySetResult();
                await releaseWrite.Task.ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                _sent.Enqueue(message);
                return true;
            });
        var cancellation = client.CancelSessionAsync(new SessionCancelParams("session-1"), TestContext.Current.CancellationToken);

        try
        {
            await writeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            await ReconnectForSessionCancellationAsync(client);
            await RaisePendingSessionRequestAsync(client, method, 402);

            // Act
            releaseWrite.TrySetResult();
            var failure = await Record.ExceptionAsync(() => cancellation.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));

            // Assert
            Assert.Empty(_sent.Select(_parser.ParseMessage).OfType<JsonRpcResponse>());
            sessionStore.Verify(store => store.CancelSessionAsync(It.IsAny<string>()), Times.Never);
            Assert.IsAssignableFrom<OperationCanceledException>(failure);
            Assert.True(await CompleteSessionRequestAsync(client, method, 402));
            Assert.False(Assert.Single(_sent.Select(_parser.ParseMessage).OfType<JsonRpcResponse>()).IsError);
        }
        finally
        {
            releaseWrite.TrySetResult();
        }
    }

    [Fact]
    public async Task SessionCancel_SendReturnsFalse_LeavesPendingRequestAndStoreUntouched()
    {
        // Arrange
        var sessionStore = new Mock<IAcpClientSessionStore>();
        using var client = await CreateInitializedClientAsync(sessionStore: sessionStore.Object);
        SetupResponseRecording();
        await RaisePendingSessionRequestAsync(client, "session/request_permission", 403);
        _transportMock.Setup(transport => transport.SendMessageAsync(It.IsRegex("session/cancel"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var failure = await Record.ExceptionAsync(() => client.CancelSessionAsync(
            new SessionCancelParams("session-1"), TestContext.Current.CancellationToken));

        // Assert
        Assert.Empty(_sent.Select(_parser.ParseMessage).OfType<JsonRpcResponse>());
        sessionStore.Verify(store => store.CancelSessionAsync(It.IsAny<string>()), Times.Never);
        Assert.Contains("session/cancel", Assert.IsType<InvalidOperationException>(failure).Message, StringComparison.Ordinal);
        Assert.True(await CompleteSessionRequestAsync(client, "session/request_permission", 403));
    }

    private bool IsResponseFrame(string message) => _parser.ParseMessage(message) is JsonRpcResponse;

    private void SetupResponseRecording()
        => _transportMock.Setup(transport => transport.SendMessageAsync(
                It.Is<string>(message => IsResponseFrame(message)), It.IsAny<CancellationToken>()))
            .Returns<string, CancellationToken>((message, token) =>
            {
                token.ThrowIfCancellationRequested();
                _sent.Enqueue(message);
                return Task.FromResult(true);
            });

    private static async Task ReconnectForSessionCancellationAsync(AcpClient client)
    {
        await client.DisconnectAsync();
        await client.InitializeAsync(new InitializeParams(new ClientInfo("reconnected", "1.0"),
            new ClientCapabilities(fs: new FsCapability()))
        {
            ProtocolVersion = AcpProtocolVersion.V1
        }, TestContext.Current.CancellationToken);
    }

    private async Task RaisePendingSessionRequestAsync(AcpClient client, string method, long requestId)
    {
        var published = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.PermissionRequestReceived += (_, _) => published.TrySetResult();
        client.FileSystemRequestReceived += (_, _) => published.TrySetResult();
        var payload = method == "session/request_permission"
            ? """{"sessionId":"session-1","toolCall":{"toolCallId":"call","title":"Read file"},"options":[{"optionId":"allow","name":"Allow","kind":"allow_once"}]}"""
            : """{"sessionId":"session-1","path":"/workspace/file.txt"}""";
        using var document = JsonDocument.Parse(payload);
        RaiseTransportMessage(_parser.SerializeMessage(new JsonRpcRequest(requestId, method, document.RootElement)));
        await published.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    private static Task<bool> CompleteSessionRequestAsync(AcpClient client, string method, long requestId)
        => method == "session/request_permission"
            ? client.RespondToPermissionRequestAsync(requestId, "selected", "allow")
            : client.RespondToFileSystemRequestAsync(requestId, success: true, content: "new request completed");

    private async Task<AcpClient> CreateInitializedClientAsync(
        AgentCapabilities? capabilities = null,
        IAcpClientSessionStore? sessionStore = null,
        ClientCapabilities? clientCapabilities = null)
    {
        var client = new AcpClient(_transportMock.Object, _loggerMock.Object, sessionStore);
        SetupInitializeResponse(capabilities);

        await client.InitializeAsync(new InitializeParams(
            new ClientInfo("Test", "1.0.0"),
            clientCapabilities ?? new ClientCapabilities())
        {
            ProtocolVersion = AcpProtocolVersion.V1
        });

        return client;
    }

    private void SetupInitializeResponse(AgentCapabilities? capabilities = null)
    {
        _transportMock
            .Setup(t => t.SendMessageAsync(It.IsRegex("initialize"), It.IsAny<CancellationToken>()))
            .Returns<string, CancellationToken>((message, _) =>
            {
                _sent.Enqueue(message);
                var request = _parser.ParseRequest(message);
                RaiseTransportMessage(_parser.SerializeMessage(new JsonRpcResponse(
                    request.Id,
                    JsonSerializer.SerializeToElement(
                        new InitializeResponse(
                            AcpProtocolVersion.V1,
                            new AgentInfo("TestAgent", "1.0.0"),
                            capabilities ?? new AgentCapabilities()),
                        AcpJsonContext.Default.InitializeResponse))));
                return Task.FromResult(true);
            });

    }

    /// <summary>
    /// Accepts the request but never answers it, so it is genuinely in flight when the caller
    /// cancels.
    /// </summary>
    private void SetupSilentSend(string methodPattern)
        => _transportMock
            .Setup(t => t.SendMessageAsync(It.IsRegex(methodPattern), It.IsAny<CancellationToken>()))
            .Returns<string, CancellationToken>((message, _) =>
            {
                _sent.Enqueue(message);
                return Task.FromResult(true);
            });

    private void RaiseTransportMessage(string message)
        => _transportMock.Raise(
            t => t.MessageReceived += null,
            new AcpTransportMessageReceivedEventArgs(message));

    private JsonDocument[] SentFrames(string method)
        => _sent
            .Select(static message => JsonDocument.Parse(message))
            .Where(document =>
                document.RootElement.TryGetProperty("method", out var sentMethod)
                && string.Equals(sentMethod.GetString(), method, StringComparison.Ordinal))
            .ToArray();

    private JsonDocument[] SentRequests(string method)
        => SentFrames(method)
            .Where(static document => document.RootElement.TryGetProperty("id", out _))
            .ToArray();

    private JsonDocument[] SentNotifications(string method)
        => SentFrames(method)
            .Where(static document => !document.RootElement.TryGetProperty("id", out _))
            .ToArray();

    private Task WaitForSentMethodAsync(string method)
        => WaitAsync(() => SentFrames(method).Length > 0, $"a sent '{method}' frame");

    private async Task<JsonDocument> WaitForSentNotificationAsync(string method)
    {
        await WaitAsync(() => SentNotifications(method).Length > 0, $"a sent '{method}' notification");
        return Assert.Single(SentNotifications(method));
    }

    private Task WaitForLoggedCodeAsync(string code)
        => WaitAsync(
            () =>
            {
                try
                {
                    _loggerMock.Verify(
                        logger => logger.Log(
                            It.IsAny<AcpClientLogLevel>(),
                            code,
                            It.IsAny<string>(),
                            It.IsAny<string?>(),
                            It.IsAny<Exception?>()),
                        Times.AtLeastOnce);
                    return true;
                }
                catch (MockException)
                {
                    return false;
                }
            },
            $"log entry '{code}'");

    private static async Task WaitAsync(Func<bool> condition, string description)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException($"Timed out waiting for {description}.");
    }
}
