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
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Acp.Serialization;
using Xunit;

namespace SalmonEgg.Acp.Tests.Client;

/// <summary>
/// Contracts for the inbound <c>elicitation/create</c> request and <c>elicitation/complete</c>
/// notification, including the fail-closed capability gates.
/// </summary>
public sealed class AcpClientElicitationTests
{
    private const string FormParamsJson = """
    {
      "sessionId": "session-1",
      "mode": "form",
      "message": "Pick a strategy",
      "requestedSchema": {
        "type": "object",
        "properties": {
          "strategy": { "type": "string", "enum": ["safe", "bold"] },
          "batch": { "type": "integer" },
          "targets": { "type": "array", "items": { "type": "string", "enum": ["api", "ui"] } }
        },
        "required": ["strategy"]
      }
    }
    """;

    private const string UrlParamsJson = """
    {
      "requestId": 12,
      "mode": "url",
      "elicitationId": "oauth-1",
      "url": "https://agent.example.com/connect",
      "message": "Authorize access"
    }
    """;

    private readonly Mock<IAcpTransport> _transportMock = new();
    private readonly Mock<IAcpClientLogger> _loggerMock = new();

    public AcpClientElicitationTests()
    {
        _transportMock.SetupGet(t => t.IsConnected).Returns(true);
    }

    [Fact]
    public async Task ElicitationCreate_WhenFormAdvertised_DeliversRequestAndSendsAcceptedContent()
    {
        var parser = new MessageParser();
        using var client = await CreateInitializedClientAsync(ClientCapabilityDefaults.Create());
        var sentMessages = CaptureSentMessages();
        ElicitationRequestEventArgs? published = null;

        client.ElicitationRequestReceived += async (_, args) =>
        {
            published = args;
            var content = new ElicitationAcceptContent()
                .SetString("strategy", "bold")
                .SetInteger("batch", 20)
                .SetStringArray("targets", ["api", "ui"]);
            await args.Accept(content);
        };

        RaiseRequest(parser, 301, ElicitationMethods.Create, FormParamsJson);
        var response = await WaitForResponseAsync(parser, sentMessages, 301);

        Assert.NotNull(published);
        Assert.Equal("session-1", published!.SessionId);
        var form = Assert.IsType<FormElicitationRequest>(published.Request);
        Assert.Equal(["strategy"], form.RequestedSchema.Required);
        Assert.False(response.IsError);

        var accepted = Assert.IsType<ElicitationAcceptResponse>(
            response.Result!.Value.Deserialize(AcpJsonContext.Default.CreateElicitationResponse));
        Assert.Equal(ElicitationActions.Accept, accepted.Action);
        Assert.Equal("\"bold\"", accepted.Content!["strategy"].RawValue.GetRawText());

        // An integer field must stay a JSON number and a multi-select a JSON array, otherwise the agent's
        // own re-validation against its requested schema rejects the submission.
        Assert.Equal(JsonValueKind.Number, accepted.Content["batch"].RawValue.ValueKind);
        Assert.Equal("20", accepted.Content["batch"].RawValue.GetRawText());
        Assert.Equal(JsonValueKind.Array, accepted.Content["targets"].RawValue.ValueKind);
        Assert.Equal("""["api","ui"]""", accepted.Content["targets"].RawValue.GetRawText());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ElicitationCreate_DeclineAndCancel_SendOmitContent(bool decline)
    {
        var parser = new MessageParser();
        using var client = await CreateInitializedClientAsync(ClientCapabilityDefaults.Create());
        var sentMessages = CaptureSentMessages();

        client.ElicitationRequestReceived += async (_, args) =>
        {
            if (decline)
            {
                await args.Decline();
            }
            else
            {
                await args.Cancel();
            }
        };

        RaiseRequest(parser, 302, ElicitationMethods.Create, FormParamsJson);
        var response = await WaitForResponseAsync(parser, sentMessages, 302);

        Assert.False(response.IsError);
        var raw = response.Result!.Value.GetRawText();
        Assert.Equal(decline ? """{"action":"decline"}""" : """{"action":"cancel"}""", raw);
    }

    [Fact]
    public async Task ElicitationCreate_SecondResponseAttempt_IsRejectedSoOneRequestGetsOneAnswer()
    {
        var parser = new MessageParser();
        using var client = await CreateInitializedClientAsync(ClientCapabilityDefaults.Create());
        var sentMessages = CaptureSentMessages();
        var firstSucceeded = false;
        var secondSucceeded = true;

        client.ElicitationRequestReceived += async (_, args) =>
        {
            firstSucceeded = await args.Decline();
            secondSucceeded = await args.Accept(new ElicitationAcceptContent().SetString("strategy", "safe"));
        };

        RaiseRequest(parser, 303, ElicitationMethods.Create, FormParamsJson);
        await WaitForResponseAsync(parser, sentMessages, 303);

        Assert.True(firstSucceeded);
        Assert.False(secondSucceeded);
    }

    [Fact]
    public async Task ElicitationCreate_WhenElicitationNotAdvertised_ReturnsMethodNotFound()
    {
        var parser = new MessageParser();
        using var client = await CreateInitializedClientAsync(new ClientCapabilities());
        var sentMessages = CaptureSentMessages();
        var delivered = false;
        client.ElicitationRequestReceived += (_, _) => delivered = true;

        RaiseRequest(parser, 304, ElicitationMethods.Create, FormParamsJson);
        var response = await WaitForResponseAsync(parser, sentMessages, 304);

        Assert.True(response.IsError);
        Assert.Equal(JsonRpcErrorCode.MethodNotFound, response.Error!.Code);
        Assert.False(delivered);
    }

    [Fact]
    public async Task ElicitationCreate_WhenModeNotAdvertised_ReturnsInvalidParams()
    {
        var parser = new MessageParser();
        // Form is advertised, URL is not: the family exists, so the refusal is a mode-level -32602 rather
        // than -32601, exactly as the elicitation spec prescribes.
        using var client = await CreateInitializedClientAsync(ClientCapabilityDefaults.Create());
        var sentMessages = CaptureSentMessages();
        var delivered = false;
        client.ElicitationRequestReceived += (_, _) => delivered = true;

        RaiseRequest(parser, 305, ElicitationMethods.Create, UrlParamsJson);
        var response = await WaitForResponseAsync(parser, sentMessages, 305);

        Assert.True(response.IsError);
        Assert.Equal(JsonRpcErrorCode.InvalidParams, response.Error!.Code);
        Assert.Contains("was not advertised by the client", response.Error.Message, StringComparison.Ordinal);
        Assert.False(delivered);
    }

    [Theory]
    [InlineData("_vendorWizard")]
    [InlineData("futureMode")]
    public async Task ElicitationCreate_WithUnknownMode_IsNeverDeliveredAsAKnownMode(string mode)
    {
        var parser = new MessageParser();
        using var client = await CreateInitializedClientAsync(
            new ClientCapabilities
            {
                Elicitation = new ElicitationCapabilities
                {
                    Form = new ElicitationFormCapabilities(),
                    Url = new ElicitationUrlCapabilities()
                }
            });
        var sentMessages = CaptureSentMessages();
        var delivered = false;
        client.ElicitationRequestReceived += (_, _) => delivered = true;

        RaiseRequest(
            parser,
            306,
            ElicitationMethods.Create,
            $$"""{"sessionId":"s","mode":"{{mode}}","message":"m"}""");
        var response = await WaitForResponseAsync(parser, sentMessages, 306);

        // Advertising both known modes still does not advertise an unknown one, so the client refuses it
        // rather than guessing which control to render.
        Assert.True(response.IsError);
        Assert.Equal(JsonRpcErrorCode.InvalidParams, response.Error!.Code);
        Assert.False(delivered);
    }

    [Fact]
    public async Task ElicitationCreate_WhenNoHandlerSubscribed_ReturnsCapabilityNotSupported()
    {
        var parser = new MessageParser();
        using var client = await CreateInitializedClientAsync(ClientCapabilityDefaults.Create());
        var sentMessages = CaptureSentMessages();

        RaiseRequest(parser, 307, ElicitationMethods.Create, FormParamsJson);
        var response = await WaitForResponseAsync(parser, sentMessages, 307);

        Assert.True(response.IsError);
        Assert.Equal(JsonRpcErrorCode.CapabilityNotSupported, response.Error!.Code);
    }

    [Fact]
    public async Task ElicitationComplete_UnknownOrMalformedIds_DoNotRaiseCompletion()
    {
        var parser = new MessageParser();
        using var client = await CreateInitializedClientAsync(UrlCapabilities());
        var completions = new List<string>();
        client.ElicitationCompleted += (_, args) => completions.Add(args.ElicitationId);

        RaiseNotification(parser, ElicitationMethods.Complete, """{"elicitationId":"oauth-1"}""");
        RaiseNotification(parser, ElicitationMethods.Complete, """{"elicitationId":""}""");
        RaiseNotification(parser, ElicitationMethods.Complete, """{}""");

        Assert.Empty(completions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ElicitationComplete_KnownUrlBeforeSuccessfulReply_RecordsExternalCompletionOnce(bool failedReply)
    {
        var parser = new MessageParser();
        using var client = await CreateInitializedClientAsync(UrlCapabilities());
        var sentMessages = CaptureSentMessages();
        var completions = new List<string>();
        client.ElicitationCompleted += (_, args) => completions.Add(args.ElicitationId);
        var request = ReceiveRequest(client, parser, 317, UrlParamsJson);
        if (failedReply)
        {
            _transportMock
                .Setup(t => t.SendMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);
            Assert.False(await request.Accept(null));
            sentMessages = CaptureSentMessages();
        }

        // Completion is the peer's external fact, not consent to navigate. Correlate the known URL
        // even before its JSON-RPC reply succeeds, while leaving the user's response independent.
        RaiseNotification(parser, ElicitationMethods.Complete, """{"elicitationId":"oauth-1"}""");
        RaiseNotification(parser, ElicitationMethods.Complete, """{"elicitationId":"oauth-1"}""");
        Assert.Equal(["oauth-1"], completions);
        Assert.Empty(sentMessages);
        Assert.True(await request.Cancel());
        var response = await WaitForResponseAsync(parser, sentMessages, 317);
        Assert.Equal("""{"action":"cancel"}""", response.Result!.Value.GetRawText());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ElicitationComplete_AcceptedUrl_RaisesOnceEvenWhenCompletionRacesResponse(bool completeDuringSend)
    {
        var parser = new MessageParser();
        using var client = await CreateInitializedClientAsync(UrlCapabilities());
        var sentMessages = CaptureSentMessages();
        var completions = new List<string>();
        client.ElicitationCompleted += (_, args) => completions.Add(args.ElicitationId);
        var request = ReceiveRequest(client, parser, 308, UrlParamsJson);
        if (completeDuringSend)
        {
            _transportMock
                .Setup(t => t.SendMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback<string, CancellationToken>((message, _) =>
                {
                    sentMessages.Enqueue(message);
                    RaiseNotification(parser, ElicitationMethods.Complete, """{"elicitationId":"oauth-1"}""");
                })
                .ReturnsAsync(true);
        }

        Assert.True(await request.Accept(null));
        var response = await WaitForResponseAsync(parser, sentMessages, 308);
        Assert.Equal("""{"action":"accept"}""", response.Result!.Value.GetRawText());
        if (!completeDuringSend)
        {
            Assert.Empty(completions);
        }

        RaiseNotification(parser, ElicitationMethods.Complete, """{"elicitationId":"oauth-1"}""");
        RaiseNotification(parser, ElicitationMethods.Complete, """{"elicitationId":"oauth-1"}""");

        Assert.Equal(["oauth-1"], completions);
        Assert.False(await request.Cancel());
        Assert.Empty(sentMessages);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ElicitationComplete_DeclinedOrCancelledUrl_DoesNotRaiseCompletion(bool decline)
    {
        var parser = new MessageParser();
        using var client = await CreateInitializedClientAsync(UrlCapabilities());
        var sentMessages = CaptureSentMessages();
        var completions = new List<string>();
        client.ElicitationCompleted += (_, args) => completions.Add(args.ElicitationId);
        var request = ReceiveRequest(client, parser, 309, UrlParamsJson);

        Assert.True(await (decline ? request.Decline() : request.Cancel()));
        var response = await WaitForResponseAsync(parser, sentMessages, 309);
        Assert.Equal(decline ? """{"action":"decline"}""" : """{"action":"cancel"}""", response.Result!.Value.GetRawText());
        RaiseNotification(parser, ElicitationMethods.Complete, """{"elicitationId":"oauth-1"}""");

        Assert.Empty(completions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ElicitationComplete_RejectedUrl_DoesNotRaiseCompletion(bool advertiseUrl)
    {
        var parser = new MessageParser();
        using var client = await CreateInitializedClientAsync(advertiseUrl ? UrlCapabilities() : ClientCapabilityDefaults.Create());
        var sentMessages = CaptureSentMessages();
        var completions = new List<string>();
        client.ElicitationCompleted += (_, args) => completions.Add(args.ElicitationId);

        RaiseRequest(parser, 310, ElicitationMethods.Create, UrlParamsJson);
        var response = await WaitForResponseAsync(parser, sentMessages, 310);
        Assert.Equal(advertiseUrl ? JsonRpcErrorCode.CapabilityNotSupported : JsonRpcErrorCode.InvalidParams, response.Error!.Code);
        RaiseNotification(parser, ElicitationMethods.Complete, """{"elicitationId":"oauth-1"}""");

        Assert.Empty(completions);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ElicitationResponse_WhenSendingFails_AllowsRetryWithoutAnsweringTwice(bool url, bool throws)
    {
        var parser = new MessageParser();
        using var client = await CreateInitializedClientAsync(UrlCapabilities());
        var sentMessages = new ConcurrentQueue<string>();
        var attempts = 0;
        _transportMock
            .Setup(t => t.SendMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<string, CancellationToken>((message, _) =>
            {
                if (++attempts == 1)
                {
                    return throws ? Task.FromException<bool>(new IOException("Response write failed.")) : Task.FromResult(false);
                }

                sentMessages.Enqueue(message);
                return Task.FromResult(true);
            });
        var request = ReceiveRequest(client, parser, 311, url ? UrlParamsJson : FormParamsJson);

        Assert.False(await request.Accept(null));
        Assert.Empty(sentMessages);
        Assert.True(await request.Accept(null));
        var response = await WaitForResponseAsync(parser, sentMessages, 311);
        Assert.Equal("""{"action":"accept"}""", response.Result!.Value.GetRawText());
        Assert.False(await request.Decline());
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task ElicitationResponse_ConcurrentActions_SendOnlyOneResponse()
    {
        var parser = new MessageParser();
        using var client = await CreateInitializedClientAsync(UrlCapabilities());
        var sentMessages = new ConcurrentQueue<string>();
        var sendResult = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _transportMock
            .Setup(t => t.SendMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((message, _) => sentMessages.Enqueue(message))
            .Returns(sendResult.Task);
        var request = ReceiveRequest(client, parser, 312, UrlParamsJson);
        var accepted = request.Accept(null);

        try
        {
            Assert.False(await request.Cancel());
            Assert.Single(sentMessages);
        }
        finally
        {
            sendResult.TrySetResult(true);
        }

        Assert.True(await accepted);
        var response = await WaitForResponseAsync(parser, sentMessages, 312);
        Assert.Equal("""{"action":"accept"}""", response.Result!.Value.GetRawText());
        Assert.Empty(sentMessages);
    }

    [Fact]
    public async Task ElicitationResponse_SessionCancelledWhileAcceptIsSending_DoesNotSendASecondResponse()
    {
        var parser = new MessageParser();
        using var client = await CreateInitializedClientAsync(UrlCapabilities());
        var sentMessages = new ConcurrentQueue<string>();
        var sendResult = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _transportMock
            .Setup(t => t.SendMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<string, CancellationToken>((message, _) =>
            {
                sentMessages.Enqueue(message);
                return parser.ParseMessage(message) is JsonRpcResponse response
                    && response.Result?.GetProperty("action").GetString() == ElicitationActions.Accept
                        ? sendResult.Task
                        : Task.FromResult(true);
            });
        var request = ReceiveRequest(client, parser, 323, FormParamsJson);
        var accept = request.Accept(null);

        try
        {
            await client.CancelSessionAsync(new SessionCancelParams("session-1"), TestContext.Current.CancellationToken);
            var messages = sentMessages.Select(parser.ParseMessage).ToArray();
            var response = Assert.Single(messages.OfType<JsonRpcResponse>());
            Assert.Equal("""{"action":"accept"}""", response.Result!.Value.GetRawText());
            Assert.Equal("session/cancel", Assert.Single(messages.OfType<JsonRpcNotification>()).Method);
        }
        finally
        {
            sendResult.TrySetResult(true);
        }

        Assert.True(await accept);
        Assert.False(await request.Cancel());
        Assert.Equal(2, sentMessages.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ElicitationResponse_SessionCancelledWhileAcceptFails_SendsCancellationOnce(bool throws)
    {
        var parser = new MessageParser();
        using var client = await CreateInitializedClientAsync(UrlCapabilities());
        var sentMessages = new ConcurrentQueue<string>();
        var sendResult = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var acceptAttempts = 0;
        _transportMock
            .Setup(t => t.SendMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<string, CancellationToken>((message, _) =>
            {
                if (parser.ParseMessage(message) is JsonRpcResponse response
                    && response.Result?.GetProperty("action").GetString() == ElicitationActions.Accept)
                {
                    acceptAttempts++;
                    return sendResult.Task;
                }

                sentMessages.Enqueue(message);
                return Task.FromResult(true);
            });
        var completions = new List<string>();
        client.ElicitationCompleted += (_, args) => completions.Add(args.ElicitationId);
        var request = ReceiveRequest(client, parser, 325,
            UrlParamsJson.Replace("\"requestId\": 12", "\"sessionId\": \"session-1\"", StringComparison.Ordinal));
        var accept = request.Accept(null);

        try
        {
            await client.CancelSessionAsync(new SessionCancelParams("session-1"), TestContext.Current.CancellationToken);
            Assert.Empty(sentMessages.Select(parser.ParseMessage).OfType<JsonRpcResponse>());
        }
        finally
        {
            if (throws)
            {
                sendResult.TrySetException(new IOException("Response write failed."));
            }
            else
            {
                sendResult.TrySetResult(false);
            }
        }

        Assert.False(await accept);
        var messages = sentMessages.Select(parser.ParseMessage).ToArray();
        Assert.Equal("session/cancel", Assert.Single(messages.OfType<JsonRpcNotification>()).Method);
        var cancelled = Assert.Single(messages.OfType<JsonRpcResponse>());
        Assert.Equal("""{"action":"cancel"}""", cancelled.Result!.Value.GetRawText());
        Assert.False(await request.Accept(null));
        Assert.False(await request.Cancel());
        RaiseNotification(parser, ElicitationMethods.Complete, """{"elicitationId":"oauth-1"}""");
        Assert.Empty(completions);
        Assert.Equal(1, acceptAttempts);
        Assert.Equal(2, sentMessages.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ElicitationResponse_SessionCancellationSendFails_RequiresExplicitCancelRetry(bool throws)
    {
        var parser = new MessageParser();
        using var client = await CreateInitializedClientAsync(UrlCapabilities());
        var sentMessages = new ConcurrentQueue<string>();
        var sendResult = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var acceptAttempts = 0;
        var cancelAttempts = 0;
        _transportMock
            .Setup(t => t.SendMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<string, CancellationToken>((message, _) =>
            {
                if (parser.ParseMessage(message) is JsonRpcResponse response)
                {
                    if (response.Result?.GetProperty("action").GetString() == ElicitationActions.Accept)
                    {
                        acceptAttempts++;
                        return sendResult.Task;
                    }

                    if (++cancelAttempts == 1)
                    {
                        return throws ? Task.FromException<bool>(new IOException("Cancellation write failed.")) : Task.FromResult(false);
                    }
                }

                sentMessages.Enqueue(message);
                return Task.FromResult(true);
            });
        var request = ReceiveRequest(client, parser, 326, FormParamsJson);
        var accept = request.Accept(null);

        try
        {
            await client.CancelSessionAsync(new SessionCancelParams("session-1"), TestContext.Current.CancellationToken);
        }
        finally
        {
            sendResult.TrySetResult(false);
        }

        Assert.False(await accept);
        Assert.Equal(1, cancelAttempts);
        Assert.Empty(sentMessages.Select(parser.ParseMessage).OfType<JsonRpcResponse>());
        Assert.False(await request.Accept(null));
        Assert.False(await request.Decline());
        Assert.Equal(1, acceptAttempts);
        Assert.Equal(1, cancelAttempts);

        Assert.True(await request.Cancel());
        var cancelled = Assert.Single(sentMessages.Select(parser.ParseMessage).OfType<JsonRpcResponse>());
        Assert.Equal("""{"action":"cancel"}""", cancelled.Result!.Value.GetRawText());
        Assert.False(await request.Cancel());
        Assert.Equal(2, cancelAttempts);
    }

    [Fact]
    public async Task CancelSessionAsync_WhenAnotherSessionReusesPendingMessageId_DoesNotCancelNewRequest()
    {
        // Arrange: whichever request the dictionary visits first holds the cancellation loop.
        var parser = new MessageParser();
        using var client = await CreateInitializedClientAsync(ClientCapabilityDefaults.Create());
        var sentMessages = new ConcurrentQueue<string>();
        var firstCancellation = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCancellation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _transportMock
            .Setup(t => t.SendMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<string, CancellationToken>((message, cancellationToken) =>
            {
                sentMessages.Enqueue(message);
                if (parser.ParseMessage(message) is JsonRpcResponse response
                    && response.Result?.GetProperty("action").GetString() == ElicitationActions.Cancel
                    && firstCancellation.TrySetResult(response.Id!.ToString()!))
                {
                    return releaseCancellation.Task.WaitAsync(cancellationToken);
                }

                return Task.FromResult(true);
            });
        var first = ReceiveRequest(client, parser, 327, FormParamsJson);
        var second = ReceiveRequest(client, parser, 328, FormParamsJson);
        var cancellation = client.CancelSessionAsync(new SessionCancelParams("session-1"), TestContext.Current.CancellationToken);

        try
        {
            // Act: finish the other request and reuse its ID on a different session before resuming.
            var cancellingId = await firstCancellation.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            var previous = cancellingId == first.MessageId.ToString() ? second : first;
            var reusedId = ReferenceEquals(previous, first) ? 327L : 328L;
            Assert.True(await previous.Accept(null));
            var current = ReceiveRequest(client, parser, reusedId,
                FormParamsJson.Replace("\"session-1\"", "\"session-2\"", StringComparison.Ordinal));
            releaseCancellation.TrySetResult(true);
            await cancellation.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            // Assert on serialized responses before the new request receives any user action.
            var response = Assert.Single(sentMessages.Select(parser.ParseMessage).OfType<JsonRpcResponse>(),
                message => message.Id?.ToString() == previous.MessageId.ToString());
            Assert.Equal("""{"action":"accept"}""", response.Result!.Value.GetRawText());
            Assert.True(await current.Accept(null));
            var reusedResponses = sentMessages.Select(parser.ParseMessage).OfType<JsonRpcResponse>()
                .Where(message => message.Id?.ToString() == previous.MessageId.ToString()).ToArray();
            Assert.Equal(2, reusedResponses.Length);
            Assert.All(reusedResponses, message => Assert.Equal("""{"action":"accept"}""", message.Result!.Value.GetRawText()));
            Assert.Equal(4, sentMessages.Count);
        }
        finally
        {
            releaseCancellation.TrySetResult(true);
            await cancellation.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        }
    }

    [Fact]
    public async Task ElicitationResponse_SessionCancelledBeforeResponse_SendsCancelAndDropsUrlCompletion()
    {
        var parser = new MessageParser();
        using var client = await CreateInitializedClientAsync(UrlCapabilities());
        var sentMessages = CaptureSentMessages();
        var completions = new List<string>();
        client.ElicitationCompleted += (_, args) => completions.Add(args.ElicitationId);
        var request = ReceiveRequest(client, parser, 324,
            UrlParamsJson.Replace("\"requestId\": 12", "\"sessionId\": \"session-1\"", StringComparison.Ordinal));

        await client.CancelSessionAsync(new SessionCancelParams("session-1"), TestContext.Current.CancellationToken);

        var messages = sentMessages.Select(parser.ParseMessage).ToArray();
        Assert.Equal("session/cancel", Assert.Single(messages.OfType<JsonRpcNotification>()).Method);
        var response = Assert.Single(messages.OfType<JsonRpcResponse>());
        Assert.Equal("""{"action":"cancel"}""", response.Result!.Value.GetRawText());
        Assert.False(await request.Accept(null));
        RaiseNotification(parser, ElicitationMethods.Complete, """{"elicitationId":"oauth-1"}""");
        Assert.Empty(completions);
        Assert.Equal(2, sentMessages.Count);
    }

    [Theory]
    [InlineData("explicit")]
    [InlineData("error")]
    [InlineData("silent")]
    public async Task ElicitationState_DisconnectedConnection_DropsCompletionsAndPendingCallbacks(string disconnect)
    {
        var parser = new MessageParser();
        using var client = await CreateInitializedClientAsync(UrlCapabilities());
        var sentMessages = CaptureSentMessages();
        var completions = new List<string>();
        client.ElicitationCompleted += (_, args) => completions.Add(args.ElicitationId);
        var accepted = ReceiveRequest(client, parser, 313, UrlParamsJson);
        var pending = ReceiveRequest(client, parser, 314, FormParamsJson);
        Assert.True(await accepted.Accept(null));
        await WaitForResponseAsync(parser, sentMessages, 313);
        _transportMock.SetupGet(t => t.IsConnected).Returns(false);

        if (disconnect == "explicit")
        {
            await client.DisconnectAsync();
        }
        else if (disconnect == "error")
        {
            _transportMock.Raise(t => t.ErrorOccurred += null,
                new AcpTransportErrorEventArgs("Connection closed.", kind: AcpTransportErrorKind.NotConnected));
        }
        else
        {
            await WaitUntilAsync(() => !client.IsInitialized);
        }

        RaiseNotification(parser, ElicitationMethods.Complete, """{"elicitationId":"oauth-1"}""");
        Assert.Empty(completions);
        Assert.False(await pending.Accept(null));
        Assert.Empty(sentMessages);
        Assert.False(client.IsInitialized);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ElicitationResponse_ReusedMessageId_OldCallbackCannotAnswerNewRequest(bool reconnect)
    {
        var parser = new MessageParser();
        using var client = await CreateInitializedClientAsync(UrlCapabilities());
        CaptureSentMessages();
        var previous = ReceiveRequest(client, parser, 315, FormParamsJson);
        if (reconnect)
        {
            await client.DisconnectAsync();
            await InitializeClientAsync(client, UrlCapabilities());
        }
        else
        {
            Assert.True(await previous.Cancel());
        }

        var sentMessages = CaptureSentMessages();
        var current = ReceiveRequest(client, parser, 315, FormParamsJson);
        Assert.False(await previous.Accept(null));
        Assert.Empty(sentMessages);
        Assert.True(await current.Accept(null));
        var response = await WaitForResponseAsync(parser, sentMessages, 315);
        Assert.Equal("""{"action":"accept"}""", response.Result!.Value.GetRawText());
        Assert.False(await previous.Cancel());
        Assert.Empty(sentMessages);
    }

    [Fact]
    public async Task ElicitationResponse_PreviousConnectionSendFails_DoesNotRestoreItsPendingRequest()
    {
        var parser = new MessageParser();
        using var client = await CreateInitializedClientAsync(UrlCapabilities());
        var sendResult = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _transportMock
            .Setup(t => t.SendMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(sendResult.Task);
        var previous = ReceiveRequest(client, parser, 316, UrlParamsJson);
        var previousSend = previous.Accept(null);

        try
        {
            await client.DisconnectAsync();
            await InitializeClientAsync(client, UrlCapabilities());
            var sentMessages = CaptureSentMessages();
            var current = ReceiveRequest(client, parser, 316, UrlParamsJson);
            sendResult.TrySetResult(false);

            Assert.False(await previousSend);
            Assert.False(await previous.Cancel());
            Assert.Empty(sentMessages);
            Assert.True(await current.Accept(null));
            var response = await WaitForResponseAsync(parser, sentMessages, 316);
            Assert.Equal("""{"action":"accept"}""", response.Result!.Value.GetRawText());
            Assert.Empty(sentMessages);
        }
        finally
        {
            sendResult.TrySetResult(false);
        }
    }

    [Fact]
    public async Task ElicitationComplete_UrlIdsAreOpaqueAndMayBeReusedAfterCompletion()
    {
        var parser = new MessageParser();
        using var client = await CreateInitializedClientAsync(UrlCapabilities());
        CaptureSentMessages();
        var completions = new List<string>();
        client.ElicitationCompleted += (_, args) => completions.Add(args.ElicitationId);
        var spacedId = UrlParamsJson.Replace("oauth-1", " oauth-1 ", StringComparison.Ordinal);
        var first = ReceiveRequest(client, parser, 318, spacedId);
        Assert.True(await first.Accept(null));
        RaiseNotification(parser, ElicitationMethods.Complete, """{"elicitationId":"oauth-1"}""");
        Assert.Empty(completions);
        RaiseNotification(parser, ElicitationMethods.Complete, """{"elicitationId":" oauth-1 "}""");
        var second = ReceiveRequest(client, parser, 319, spacedId);
        Assert.True(await second.Accept(null));
        RaiseNotification(parser, ElicitationMethods.Complete, """{"elicitationId":" oauth-1 "}""");

        Assert.Equal([" oauth-1 ", " oauth-1 "], completions);
    }

    [Fact]
    public async Task ElicitationCreate_DuplicateOutstandingUrlId_PreservesTheOriginalInteraction()
    {
        var parser = new MessageParser();
        using var client = await CreateInitializedClientAsync(UrlCapabilities());
        var sentMessages = CaptureSentMessages();
        var completions = new List<string>();
        client.ElicitationCompleted += (_, args) => completions.Add(args.ElicitationId);
        var first = ReceiveRequest(client, parser, 321, UrlParamsJson);
        var duplicateDelivered = false;
        client.ElicitationRequestReceived += (_, _) => duplicateDelivered = true;

        RaiseRequest(parser, 322, ElicitationMethods.Create, UrlParamsJson);

        var duplicate = await WaitForResponseAsync(parser, sentMessages, 322);
        Assert.Equal(JsonRpcErrorCode.InvalidParams, duplicate.Error!.Code);
        Assert.False(duplicateDelivered);
        Assert.True(await first.Accept(null));
        var accepted = await WaitForResponseAsync(parser, sentMessages, 321);
        Assert.Equal("""{"action":"accept"}""", accepted.Result!.Value.GetRawText());
        RaiseNotification(parser, ElicitationMethods.Complete, """{"elicitationId":"oauth-1"}""");
        RaiseNotification(parser, ElicitationMethods.Complete, """{"elicitationId":"oauth-1"}""");
        Assert.Equal(["oauth-1"], completions);
    }

    [Fact]
    public async Task ElicitationResponse_DisposedClient_CannotSendTheCapturedCallback()
    {
        var parser = new MessageParser();
        using var client = await CreateInitializedClientAsync(UrlCapabilities());
        var sentMessages = CaptureSentMessages();
        var request = ReceiveRequest(client, parser, 320, UrlParamsJson);

        client.Dispose();

        Assert.False(await request.Accept(null));
        Assert.False(await request.Decline());
        Assert.False(await request.Cancel());
        Assert.Empty(sentMessages);
    }

    private static ClientCapabilities UrlCapabilities()
        => new()
        {
            Elicitation = new ElicitationCapabilities
            {
                Form = new ElicitationFormCapabilities(),
                Url = new ElicitationUrlCapabilities()
            }
        };

    private ElicitationRequestEventArgs ReceiveRequest(AcpClient client, MessageParser parser, long id, string paramsJson)
    {
        ElicitationRequestEventArgs? received = null;
        void OnRequest(object? sender, ElicitationRequestEventArgs args) => received = args;
        client.ElicitationRequestReceived += OnRequest;
        try
        {
            RaiseRequest(parser, id, ElicitationMethods.Create, paramsJson);
            Assert.NotNull(received);
            return received;
        }
        finally
        {
            client.ElicitationRequestReceived -= OnRequest;
        }
    }

    private ConcurrentQueue<string> CaptureSentMessages()
    {
        var sentMessages = new ConcurrentQueue<string>();
        _transportMock
            .Setup(t => t.SendMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((message, _) => sentMessages.Enqueue(message))
            .ReturnsAsync(true);
        return sentMessages;
    }

    private void RaiseRequest(MessageParser parser, long id, string method, string paramsJson)
    {
        using var document = JsonDocument.Parse(paramsJson);
        var request = new JsonRpcRequest(id, method, document.RootElement.Clone());
        _transportMock.Raise(
            t => t.MessageReceived += null,
            new AcpTransportMessageReceivedEventArgs(parser.SerializeMessage(request)));
    }

    private void RaiseNotification(MessageParser parser, string method, string paramsJson)
    {
        using var document = JsonDocument.Parse(paramsJson);
        var notification = new JsonRpcNotification(method, document.RootElement.Clone());
        _transportMock.Raise(
            t => t.MessageReceived += null,
            new AcpTransportMessageReceivedEventArgs(parser.SerializeMessage(notification)));
    }

    private async Task<AcpClient> CreateInitializedClientAsync(ClientCapabilities clientCapabilities)
    {
        var client = new AcpClient(_transportMock.Object, _loggerMock.Object);
        await InitializeClientAsync(client, clientCapabilities);
        return client;
    }

    private async Task InitializeClientAsync(AcpClient client, ClientCapabilities clientCapabilities)
    {
        var parser = new MessageParser();

        _transportMock
            .Setup(t => t.SendMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((message, _) =>
            {
                if (parser.ParseMessage(message) is not JsonRpcRequest request
                    || !string.Equals(request.Method, "initialize", StringComparison.Ordinal))
                {
                    return;
                }

                var initResponse = new InitializeResponse(
                    AcpProtocolVersion.V1,
                    new AgentInfo("TestAgent", "1.0.0"),
                    new AgentCapabilities());
                var response = new JsonRpcResponse(
                    request.Id,
                    JsonSerializer.SerializeToElement(initResponse, AcpJsonContext.Default.InitializeResponse));
                _transportMock.Raise(
                    t => t.MessageReceived += null,
                    new AcpTransportMessageReceivedEventArgs(parser.SerializeMessage(response)));
            })
            .ReturnsAsync(true);

        await client.InitializeAsync(new InitializeParams(
            new ClientInfo("Test", "1.0.0"),
            clientCapabilities));
    }

    private static async Task<JsonRpcResponse> WaitForResponseAsync(
        MessageParser parser,
        ConcurrentQueue<string> sentMessages,
        long responseId,
        int timeoutMilliseconds = 5000)
    {
        var timeoutAt = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
        while (DateTime.UtcNow < timeoutAt)
        {
            while (sentMessages.TryDequeue(out var message))
            {
                if (parser.ParseMessage(message) is JsonRpcResponse response
                    && response.Id is not null
                    && long.TryParse(response.Id.ToString(), out var actualId)
                    && actualId == responseId)
                {
                    return response;
                }
            }

            await Task.Delay(20);
        }

        throw new TimeoutException($"Timed out waiting for JSON-RPC response {responseId}.");
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMilliseconds = 5000)
    {
        var timeoutAt = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
        while (DateTime.UtcNow < timeoutAt)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException("Timed out waiting for the expected client state.");
    }
}
