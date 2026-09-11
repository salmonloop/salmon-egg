using System.Collections.Concurrent;
using System.Text.Json;
using SalmonEgg.Acp.Client;
using SalmonEgg.Acp.JsonRpc;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Acp.Serialization;

namespace SalmonEgg.Acp.Tests.Client;

public sealed class AcpClientBatchLifecycleTests
{
    private const string AbandonedBatchCode = "BATCH_RESPONSE_ABANDONED";

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("[1]", false)]
    [InlineData("[1]", true)]
    [InlineData("[{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"_unsupported\"}]", false)]
    [InlineData("[{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"session/request_permission\",\"params\":{}}]", false)]
    public async Task Batch_ResponseFailureWithoutRetryOwner_TerminatesOnceWithoutDisconnecting(string frame, bool throwOnWrite)
    {
        // Arrange
        using var peer = await LifecyclePeer.CreateAsync();
        peer.FailResponses = true;
        peer.ThrowOnWrite = throwOnWrite;

        // Act
        peer.Deliver(frame);

        // Assert
        Assert.Equal(1, peer.ResponseAttempts);
        Assert.Equal(AcpClientLogLevel.Warning, Assert.Single(peer.Logs, entry => entry.Code == AbandonedBatchCode).Level);
        Assert.True(peer.Client.IsInitialized);
        Assert.True(peer.Client.IsConnected);
        peer.FailResponses = false;
        peer.ThrowOnWrite = false;
        peer.Deliver("[1]");
        Assert.Equal(2, peer.ResponseAttempts);
        Assert.Single(peer.Responses);
        Assert.Single(peer.Logs, entry => entry.Code == AbandonedBatchCode);
    }

    [Fact]
    public async Task Batch_UnretryableFailureLoggerThrows_ConnectionCanStillAnswerNewRequests()
    {
        // Arrange
        using var peer = await LifecyclePeer.CreateAsync();
        peer.FailResponses = true;
        peer.ThrowOnAbandonLog = true;

        // Act
        peer.Deliver("[1]");
        peer.ThrowOnAbandonLog = false;
        peer.FailResponses = false;
        peer.Deliver("[1]");

        // Assert
        Assert.Equal(2, peer.ResponseAttempts);
        Assert.Single(peer.Responses);
        Assert.Single(peer.Logs, entry => entry.Code == AbandonedBatchCode);
        Assert.True(peer.Client.IsConnected);
    }

    [Fact]
    public async Task Batch_RepeatedUnretryableFailures_ClosesEachAttemptOnTheLiveConnection()
    {
        // Arrange
        using var peer = await LifecyclePeer.CreateAsync();
        peer.FailResponses = true;

        // Act
        for (var attempt = 0; attempt < 16; attempt++) peer.Deliver("[1]");

        // Assert
        Assert.Equal(16, peer.ResponseAttempts);
        Assert.Equal(16, peer.Logs.Count(entry => entry.Code == AbandonedBatchCode));
        Assert.True(peer.Client.IsConnected);
        Assert.Empty(peer.Responses);
    }

    [Fact]
    public async Task Batch_MixedErrorAndFormFailure_PreservesTheOriginalRetryAndAllResponses()
    {
        // Arrange
        using var peer = await LifecyclePeer.CreateAsync();
        ElicitationRequestEventArgs? form = null;
        peer.Client.ElicitationRequestReceived += (_, request) => form = request;
        peer.Deliver("[1," + Form() + "]");
        Assert.NotNull(form);
        peer.FailResponses = true;

        // Act
        Assert.False(await form.Cancel().WaitAsync(TestToken));
        peer.FailResponses = false;
        Assert.True(await form.Cancel().WaitAsync(TestToken));

        // Assert
        Assert.DoesNotContain(peer.Logs, entry => entry.Code == AbandonedBatchCode);
        Assert.Equal(2, peer.ResponseAttempts);
        var response = Assert.Single(peer.Responses);
        Assert.Equal(2, response.GetArrayLength());
        Assert.Equal(JsonRpcErrorCode.InvalidRequest, response[0].GetProperty("error").GetProperty("code").GetInt32());
        Assert.Equal("cancel", response[1].GetProperty("result").GetProperty("action").GetString());
        Assert.False(await form.Cancel().WaitAsync(TestToken));
        Assert.Equal(2, peer.ResponseAttempts);
    }

    private static string Form()
        => """
           {"jsonrpc":"2.0","id":"form","method":"elicitation/create","params":{
           "mode":"form","message":"Choose","requestedSchema":{"type":"object","properties":{}}}}
           """;

    private sealed class LifecyclePeer : IAcpTransport, IAcpClientLogger
    {
        private readonly MessageParser _parser = new();

        private LifecyclePeer() => Client = new AcpClient(this, this);

        internal AcpClient Client { get; }
        public bool IsConnected { get; private set; }
        internal bool FailResponses { get; set; }
        internal bool ThrowOnWrite { get; set; }
        internal bool ThrowOnAbandonLog { get; set; }
        internal int ResponseAttempts { get; private set; }
        internal ConcurrentQueue<JsonElement> Responses { get; } = new();
        internal ConcurrentQueue<(AcpClientLogLevel Level, string Code)> Logs { get; } = new();

        public event EventHandler<AcpTransportMessageReceivedEventArgs>? MessageReceived;
        public event EventHandler<AcpTransportErrorEventArgs>? ErrorOccurred { add { } remove { } }

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
            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                ResponseAttempts++;
                if (ThrowOnWrite) throw new IOException("The response could not be sent.");
                if (FailResponses) return Task.FromResult(false);
                Responses.Enqueue(document.RootElement.Clone());
            }
            else if (_parser.ParseMessage(message) is JsonRpcRequest { Method: "initialize" } request)
            {
                var response = new InitializeResponse(AcpProtocolVersion.V2, new AgentInfo("lifecycle-peer", "1.0"), new AgentCapabilities());
                Deliver("{\"jsonrpc\":\"2.0\",\"id\":" + request.Id + ",\"result\":"
                    + JsonSerializer.Serialize(response, AcpWireFormat.For(AcpProtocolVersion.V2).TypeInfo<InitializeResponse>()) + "}");
            }
            return Task.FromResult(true);
        }

        public void Log(AcpClientLogLevel level, string code, string message, string? source = null, Exception? exception = null)
        {
            Logs.Enqueue((level, code));
            if (ThrowOnAbandonLog && code == AbandonedBatchCode) throw new InvalidOperationException("Host logging failed.");
        }

        public void Dispose() => Client.Dispose();

        internal static async Task<LifecyclePeer> CreateAsync()
        {
            var peer = new LifecyclePeer();
            await peer.Client.InitializeDraftAsync(new InitializeParams(new ClientInfo("lifecycle-test", "1.0"),
                new ClientCapabilities
                {
                    Elicitation = new ElicitationCapabilities { Form = new ElicitationFormCapabilities() }
                })
            { ProtocolVersion = AcpProtocolVersion.V2 }, TestToken);
            return peer;
        }

        internal void Deliver(string message)
            => MessageReceived?.Invoke(this, new AcpTransportMessageReceivedEventArgs(message));
    }
}
