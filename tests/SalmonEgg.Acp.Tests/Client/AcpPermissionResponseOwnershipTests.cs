using System.Text.Json;
using SalmonEgg.Acp.Client;
using SalmonEgg.Acp.JsonRpc;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Acp.Serialization;

namespace SalmonEgg.Acp.Tests.Client;

public sealed class AcpPermissionResponseOwnershipTests
{
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("selected", null)]
    [InlineData("selected", "not-offered")]
    [InlineData("", "allow")]
    [InlineData("unexpected", "allow")]
    public async Task RespondToPermissionRequestAsync_InvalidChoice_LeavesRequestRetryable(string outcome, string? optionId)
    {
        // Arrange
        using var peer = await PermissionPeer.CreateAsync();
        peer.Request();
        var request = Assert.Single(peer.Permissions);

        // Act / Assert
        var error = await Assert.ThrowsAsync<AcpException>(() => peer.Client.RespondToPermissionRequestAsync(51L, outcome, optionId));
        Assert.Equal(JsonRpcErrorCode.InvalidParams, error.ErrorCode);
        Assert.Empty(peer.Responses);
        await request.Respond("selected", "allow");
        Assert.Equal("allow", Selected(Assert.Single(peer.Responses)));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task RespondToPermissionRequestAsync_OfferedEmptyOrWhitespaceId_PreservesSchemaString(string optionId)
    {
        // Arrange
        using var peer = await PermissionPeer.CreateAsync();
        peer.Request(optionId: optionId);

        // Act
        await Assert.Single(peer.Permissions).Respond("selected", optionId);

        // Assert
        Assert.Equal(optionId, Selected(Assert.Single(peer.Responses)));
    }

    [Fact]
    public async Task PermissionRequestReceived_MutatedOptions_CannotForgeAnOfferedChoice()
    {
        // Arrange
        using var peer = await PermissionPeer.CreateAsync();
        peer.Request();
        var request = Assert.Single(peer.Permissions);
        request.Options.Clear();
        request.Options.Add(new PermissionOption("forged", "Forged", "allow_always"));

        // Act / Assert
        await Assert.ThrowsAsync<AcpException>(() => request.Respond("selected", "forged"));
        Assert.Empty(peer.Responses);
        await request.Respond("selected", "allow");
        Assert.Equal("allow", Selected(Assert.Single(peer.Responses)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PermissionRequestReceived_StaleCallbackWithReusedId_CannotConsumeNewRequest(bool reconnect)
    {
        // Arrange
        using var peer = await PermissionPeer.CreateAsync();
        peer.Request();
        var old = Assert.Single(peer.Permissions);
        if (reconnect)
        {
            await peer.Client.DisconnectAsync();
            await peer.InitializeAsync();
        }
        else
        {
            await old.Respond("cancelled", null);
        }
        peer.Request(sessionId: "replacement");
        var count = peer.Responses.Count;

        // Act
        await old.Respond("selected", "allow");

        // Assert
        Assert.Equal(count, peer.Responses.Count);
        await peer.Permissions[^1].Respond("selected", "allow");
        Assert.Equal(count + 1, peer.Responses.Count);
        Assert.Equal("allow", Selected(peer.Responses[^1]));
    }

    [Fact]
    public async Task PermissionRequestReceived_DelayedOldResponse_CannotWriteAfterReconnect()
    {
        // Arrange
        using var peer = await PermissionPeer.CreateAsync();
        peer.Request();
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        peer.ResponseSend = (_, token) => release.Task.WaitAsync(token);
        var response = peer.Permissions[0].Respond("selected", "allow");
        Assert.Single(peer.Attempts);

        try
        {
            // Act
            await peer.Client.DisconnectAsync();
            await peer.InitializeAsync();
            peer.Request(sessionId: "replacement", optionId: "current");
            release.TrySetResult(true);
            await response.WaitAsync(TestToken);

            // Assert
            Assert.Empty(peer.Responses);
            peer.ResponseSend = null;
            await peer.Permissions[^1].Respond("selected", "current");
            Assert.Equal("current", Selected(Assert.Single(peer.Responses)));
            Assert.Empty(peer.Errors);
        }
        finally
        {
            release.TrySetResult(true);
            await response.WaitAsync(TestToken);
        }
    }

    [Fact]
    public async Task RespondToPermissionRequestAsync_ConcurrentAnswers_OnlyOneResponseReachesTransport()
    {
        // Arrange
        using var peer = await PermissionPeer.CreateAsync();
        peer.Request();
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        peer.ResponseSend = (_, token) => release.Task.WaitAsync(token);
        var first = peer.Client.RespondToPermissionRequestAsync(51L, "selected", "allow");

        try
        {
            // Act / Assert
            Assert.False(await peer.Client.RespondToPermissionRequestAsync(51L, "cancelled"));
            Assert.Single(peer.Attempts);
            release.TrySetResult(true);
            Assert.True(await first.WaitAsync(TestToken));
            Assert.Single(peer.Responses);
            Assert.False(await peer.Client.RespondToPermissionRequestAsync(51L, "selected", "allow"));
        }
        finally
        {
            release.TrySetResult(true);
            await first.WaitAsync(TestToken);
        }
    }

    [Fact]
    public async Task RespondToPermissionRequestAsync_FailedSend_CanRetryOriginalRequest()
    {
        // Arrange
        using var peer = await PermissionPeer.CreateAsync();
        peer.Request();
        peer.ResponseSend = (_, _) => Task.FromResult(false);

        // Act / Assert
        Assert.False(await peer.Client.RespondToPermissionRequestAsync(51L, "selected", "allow"));
        Assert.Empty(peer.Responses);
        peer.ResponseSend = null;
        Assert.True(await peer.Client.RespondToPermissionRequestAsync(51L, "selected", "allow"));
        Assert.Equal("allow", Selected(Assert.Single(peer.Responses)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CancelSessionAsync_FailedAnswerInFlight_RetainsCancellationUntilItCanBeDelivered(bool cancellationSucceeds)
    {
        // Arrange
        using var peer = await PermissionPeer.CreateAsync();
        peer.Request();
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        peer.ResponseSend = (response, token) => Outcome(response) == "selected"
            ? release.Task.WaitAsync(token)
            : Task.FromResult(cancellationSucceeds);
        var answer = peer.Client.RespondToPermissionRequestAsync(51L, "selected", "allow");

        try
        {
            // Act
            await peer.Client.CancelSessionAsync(new SessionCancelParams("one"), TestToken);
            release.TrySetResult(false);
            Assert.False(await answer.WaitAsync(TestToken));

            // Assert
            Assert.Equal(["selected", "cancelled"], peer.Attempts.Select(Outcome));
            Assert.False(await peer.Client.RespondToPermissionRequestAsync(51L, "selected", "allow"));
            if (!cancellationSucceeds)
            {
                Assert.Empty(peer.Responses);
                peer.ResponseSend = null;
                Assert.True(await peer.Client.RespondToPermissionRequestAsync(51L, "cancelled"));
            }
            Assert.Equal("cancelled", Outcome(Assert.Single(peer.Responses)));
        }
        finally
        {
            release.TrySetResult(false);
            await answer.WaitAsync(TestToken);
        }
    }

    [Fact]
    public async Task CancelSessionAsync_PendingPermission_CancelsOnceAndRejectsOldUserCallback()
    {
        // Arrange
        using var peer = await PermissionPeer.CreateAsync();
        peer.Request();
        var request = Assert.Single(peer.Permissions);

        // Act
        await peer.Client.CancelSessionAsync(new SessionCancelParams("one"), TestToken);
        await request.Respond("selected", "allow");

        // Assert
        Assert.Equal("cancelled", Outcome(Assert.Single(peer.Responses)));
    }

    [Fact]
    public async Task RespondToPermissionRequestAsync_DifferentPendingMethod_DoesNotConsumeIt()
    {
        // Arrange
        using var peer = await PermissionPeer.CreateAsync();
        peer.Client.FileSystemRequestReceived += (_, _) => { };
        peer.Deliver("""{"jsonrpc":"2.0","id":51,"method":"fs/read_text_file","params":{"sessionId":"one","path":"/work/file"}}""");

        // Act
        var answered = await peer.Client.RespondToPermissionRequestAsync(51L, "selected", "allow");

        // Assert
        Assert.False(answered);
        Assert.Empty(peer.Responses);
        Assert.True(await peer.Client.RespondToFileSystemRequestAsync(51L, true, "content"));
        Assert.Equal("content", Assert.Single(peer.Responses).Result!.Value.GetProperty("content").GetString());
    }

    [Fact]
    public async Task PermissionRequestReceived_SubscriberThrowsAfterAnswer_DoesNotSendAnotherResponse()
    {
        // Arrange
        using var peer = await PermissionPeer.CreateAsync();
        Task? answer = null;
        peer.Client.PermissionRequestReceived += (_, request) =>
        {
            answer = request.Respond("selected", "allow");
            throw new JsonException("Host failed after answering.");
        };

        // Act
        peer.Request();
        Assert.NotNull(answer);
        await answer.WaitAsync(TestToken);

        // Assert
        Assert.Equal("allow", Selected(Assert.Single(peer.Responses)));
        Assert.Contains("Host failed after answering.", Assert.Single(peer.Errors), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PermissionRequestReceived_SubscriberThrowsWithAnswerInFlight_PreservesTheResponseClaim()
    {
        // Arrange
        using var peer = await PermissionPeer.CreateAsync();
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        peer.ResponseSend = (_, token) => release.Task.WaitAsync(token);
        Task? answer = null;
        peer.Client.PermissionRequestReceived += (_, request) =>
        {
            answer = request.Respond("selected", "allow");
            throw new InvalidOperationException("Host failed with an answer in flight.");
        };

        try
        {
            // Act
            peer.Request();
            Assert.NotNull(answer);

            // Assert
            Assert.Empty(peer.Responses);
            Assert.Single(peer.Attempts);
            release.TrySetResult(true);
            await answer.WaitAsync(TestToken);
            Assert.Equal("allow", Selected(Assert.Single(peer.Responses)));
            Assert.False(await peer.Client.RespondToPermissionRequestAsync(51L, "selected", "allow"));
        }
        finally
        {
            release.TrySetResult(true);
            if (answer is not null) await answer.WaitAsync(TestToken);
        }
    }

    [Fact]
    public async Task PermissionRequestReceived_SubscriberThrowsAfterReplacement_DoesNotDeleteNewRequest()
    {
        // Arrange
        using var peer = await PermissionPeer.CreateAsync();
        Task? cancelled = null;
        peer.Client.PermissionRequestReceived += (_, request) =>
        {
            if (request.SessionId != "one") return;
            cancelled = request.Respond("cancelled", null);
            peer.Request(sessionId: "replacement");
            throw new InvalidOperationException("Old host callback failed.");
        };

        // Act
        peer.Request();
        Assert.NotNull(cancelled);
        await cancelled.WaitAsync(TestToken);

        // Assert
        Assert.Equal("cancelled", Outcome(Assert.Single(peer.Responses)));
        await peer.Permissions[^1].Respond("selected", "allow");
        Assert.Equal(2, peer.Responses.Count);
        Assert.Equal("allow", Selected(peer.Responses[^1]));
    }

    [Fact]
    public async Task PermissionRequestReceived_FailureResponseDelayedAcrossReconnect_DoesNotWriteToNewConnection()
    {
        // Arrange
        using var peer = await PermissionPeer.CreateAsync();
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var interrupted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        peer.ResponseSend = async (_, token) =>
        {
            try
            {
                return await release.Task.WaitAsync(token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                interrupted.TrySetResult(true);
                throw;
            }
        };
        peer.Client.PermissionRequestReceived += (_, request) =>
        {
            if (request.SessionId == "one") throw new InvalidOperationException("Host display failed.");
        };
        peer.Request();
        Assert.Single(peer.Attempts);

        try
        {
            // Act
            await peer.Client.DisconnectAsync();
            await peer.InitializeAsync();
            var interruptedBeforeRelease = await Task.WhenAny(interrupted.Task, release.Task)
                .WaitAsync(TimeSpan.FromSeconds(2), TestToken);
            Assert.Same(interrupted.Task, interruptedBeforeRelease);
            release.TrySetResult(true);

            // Assert
            Assert.Empty(peer.Responses);
            peer.ResponseSend = null;
            peer.Request(sessionId: "replacement");
            await peer.Permissions[^1].Respond("selected", "allow");
            Assert.Equal("allow", Selected(Assert.Single(peer.Responses)));
        }
        finally
        {
            release.TrySetResult(false);
        }
    }

    [Fact]
    public async Task PermissionRequestReceived_SubscriberFailureSendFails_RequestRemainsRetryable()
    {
        // Arrange
        using var peer = await PermissionPeer.CreateAsync();
        peer.ResponseSend = (_, _) => Task.FromResult(false);
        peer.Client.PermissionRequestReceived += (_, _) => throw new InvalidOperationException("Host display failed.");

        // Act
        peer.Request();

        // Assert
        Assert.Equal(JsonRpcErrorCode.InternalError, Assert.Single(peer.Attempts).Error!.Code);
        Assert.Empty(peer.Responses);
        peer.ResponseSend = null;
        await Assert.Single(peer.Permissions).Respond("cancelled", null);
        Assert.Equal("cancelled", Outcome(Assert.Single(peer.Responses)));
    }

    private static string? Outcome(JsonRpcResponse response)
        => response.Result!.Value.GetProperty("outcome").GetProperty("outcome").GetString();

    private static string? Selected(JsonRpcResponse response)
    {
        Assert.Equal("selected", Outcome(response));
        return response.Result!.Value.GetProperty("outcome").GetProperty("optionId").GetString();
    }

    private sealed class PermissionPeer : IAcpTransport
    {
        private readonly MessageParser _parser = new();

        private PermissionPeer()
        {
            Client = new AcpClient(this);
            Client.PermissionRequestReceived += (_, request) => Permissions.Add(request);
            Client.ErrorOccurred += (_, error) => Errors.Add(error);
        }

        public bool IsConnected { get; private set; }
        internal AcpClient Client { get; }
        internal List<PermissionRequestEventArgs> Permissions { get; } = [];
        internal List<JsonRpcResponse> Responses { get; } = [];
        internal List<JsonRpcResponse> Attempts { get; } = [];
        internal List<string> Errors { get; } = [];
        internal Func<JsonRpcResponse, CancellationToken, Task<bool>>? ResponseSend { get; set; }

        public event EventHandler<AcpTransportMessageReceivedEventArgs>? MessageReceived;
        public event EventHandler<AcpTransportErrorEventArgs>? ErrorOccurred { add { } remove { } }

        internal static async Task<PermissionPeer> CreateAsync()
        {
            var peer = new PermissionPeer();
            await peer.InitializeAsync();
            return peer;
        }

        internal Task<InitializeResponse> InitializeAsync()
            => Client.InitializeAsync(new InitializeParams(new ClientInfo("permission-peer", "1.0"),
                new ClientCapabilities(fs: new FsCapability(readTextFile: true))), TestToken);

        public Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
        {
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
            switch (_parser.ParseMessage(message))
            {
                case JsonRpcRequest request when request.Method == "initialize":
                    var result = JsonSerializer.Serialize(new InitializeResponse(AcpProtocolVersion.V1,
                        new AgentInfo("permission-agent", "1.0"), new AgentCapabilities()), AcpJsonContext.Default.InitializeResponse);
                    Deliver("{\"jsonrpc\":\"2.0\",\"id\":" + request.Id + ",\"result\":" + result + "}");
                    break;
                case JsonRpcResponse response:
                    Attempts.Add(response);
                    if (ResponseSend is { } send && !await send(response, cancellationToken).ConfigureAwait(false)) return false;
                    cancellationToken.ThrowIfCancellationRequested();
                    Responses.Add(response);
                    break;
            }
            return true;
        }

        internal void Request(string sessionId = "one", string optionId = "allow")
            => Deliver("{\"jsonrpc\":\"2.0\",\"id\":51,\"method\":\"session/request_permission\",\"params\":{\"sessionId\":\""
                + sessionId + "\",\"toolCall\":{\"toolCallId\":\"tool\"},\"options\":[{\"optionId\":\""
                + optionId + "\",\"name\":\"Allow\",\"kind\":\"allow_once\"}]}}");

        internal void Deliver(string message) => MessageReceived?.Invoke(this, new AcpTransportMessageReceivedEventArgs(message));

        public void Dispose()
        {
            Client.Dispose();
            IsConnected = false;
        }
    }
}
