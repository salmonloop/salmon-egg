using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
        var client = await CreateInitializedClientAsync(ClientCapabilityDefaults.Create());
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
        var client = await CreateInitializedClientAsync(ClientCapabilityDefaults.Create());
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
        var client = await CreateInitializedClientAsync(ClientCapabilityDefaults.Create());
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
        var client = await CreateInitializedClientAsync(new ClientCapabilities());
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
        var client = await CreateInitializedClientAsync(ClientCapabilityDefaults.Create());
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
        var client = await CreateInitializedClientAsync(
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
        var client = await CreateInitializedClientAsync(ClientCapabilityDefaults.Create());
        var sentMessages = CaptureSentMessages();

        RaiseRequest(parser, 307, ElicitationMethods.Create, FormParamsJson);
        var response = await WaitForResponseAsync(parser, sentMessages, 307);

        Assert.True(response.IsError);
        Assert.Equal(JsonRpcErrorCode.CapabilityNotSupported, response.Error!.Code);
    }

    [Fact]
    public async Task ElicitationComplete_RaisesCompletionForKnownIdAndIgnoresMalformedPayload()
    {
        var parser = new MessageParser();
        var client = await CreateInitializedClientAsync(ClientCapabilityDefaults.Create());
        var completions = new List<string>();
        client.ElicitationCompleted += (_, args) => completions.Add(args.ElicitationId);

        RaiseNotification(parser, ElicitationMethods.Complete, """{"elicitationId":"oauth-1"}""");
        RaiseNotification(parser, ElicitationMethods.Complete, """{"elicitationId":""}""");
        RaiseNotification(parser, ElicitationMethods.Complete, """{}""");

        await WaitUntilAsync(() => completions.Count >= 1);
        Assert.Equal(["oauth-1"], completions);
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
        var parser = new MessageParser();
        var client = new AcpClient(_transportMock.Object, _loggerMock.Object);

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

        return client;
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
