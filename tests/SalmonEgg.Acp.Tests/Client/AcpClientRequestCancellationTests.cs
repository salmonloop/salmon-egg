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

    private async Task<AcpClient> CreateInitializedClientAsync(AgentCapabilities? capabilities = null)
    {
        var client = new AcpClient(_transportMock.Object, _loggerMock.Object);
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

        await client.InitializeAsync(new InitializeParams(
            new ClientInfo("Test", "1.0.0"),
            new ClientCapabilities())
        {
            ProtocolVersion = AcpProtocolVersion.V1
        });

        return client;
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
