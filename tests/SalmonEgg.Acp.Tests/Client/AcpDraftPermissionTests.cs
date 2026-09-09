using System.Text.Json;
using SalmonEgg.Acp.Client;
using SalmonEgg.Acp.JsonRpc;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Acp.Serialization;

namespace SalmonEgg.Acp.Tests.Client;

public sealed class AcpDraftPermissionTests
{
    private const string Options = "\"options\":[{\"optionId\":\"allow\",\"name\":\"Allow\",\"kind\":\"allow_once\"}]";
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("")]
    [InlineData(",\"subject\":null")]
    public async Task PermissionRequest_NoSubject_PublishesPromptAndSelectedResponse(string subject)
    {
        // Arrange
        using var peer = await PermissionPeer.CreateAsync();

        // Act
        peer.Request("{\"sessionId\":\"one\",\"title\":\"Approve operation?\",\"description\":\"Please review\","
            + Options + subject + "}");
        var request = Assert.Single(peer.Requests);
        await request.Respond("selected", "allow");

        // Assert
        var draft = request.GetDraftRequest();
        Assert.NotNull(draft);
        Assert.Equal("one", draft.SessionId);
        Assert.Equal("Approve operation?", draft.Title);
        Assert.Equal("Please review", draft.Description);
        Assert.Null(draft.Subject);
        Assert.Null(request.ToolCall);
        Assert.Equal("selected", Outcome(Assert.Single(peer.Responses)));
        Assert.Empty(peer.Errors);
    }

    [Theory]
    [InlineData("")]
    [InlineData(",\"description\":null,\"_meta\":null")]
    [InlineData(",\"description\":27,\"_meta\":[]")]
    public void ReadRequest_OptionalDefaultedFields_KeepAbsentValuesWithoutDroppingRaw(string optional)
    {
        // Arrange
        var parameters = Json("{\"sessionId\":\"\",\"title\":\"\",\"unknown\":1e2," + Options + optional + "}");

        // Act
        var request = AcpPermissionDraftExtensions.ReadRequest(parameters);

        // Assert
        Assert.Equal("", request.SessionId);
        Assert.Equal("", request.Title);
        Assert.Null(request.Description);
        Assert.Null(request.Meta);
        Assert.Equal(parameters.GetRawText(), request.RawParameters.GetRawText());
    }

    [Theory]
    [InlineData("/work")]
    [InlineData("C:\\work")]
    [InlineData("\\\\server\\share")]
    public async Task PermissionRequest_CommandSubject_ExposesContextWithoutCreatingTerminalOrTool(string cwd)
    {
        // Arrange
        using var peer = await PermissionPeer.CreateAsync();
        var subject = new CommandPermissionSubject { Command = "cargo test", Cwd = cwd, ToolCallId = "tool", TerminalId = "terminal" };
        var subjectJson = JsonSerializer.Serialize<RequestPermissionSubject>(subject,
            AcpWireFormat.For(AcpProtocolVersion.V2).TypeInfo<RequestPermissionSubject>());

        // Act
        peer.Request("{\"sessionId\":\"one\",\"title\":\"Run tests?\",\"subject\":" + subjectJson + "," + Options + "}");
        var request = Assert.Single(peer.Requests);
        await request.Respond("selected", "allow");

        // Assert
        var command = Assert.IsType<CommandPermissionSubject>(request.GetDraftRequest()!.Subject);
        Assert.Equal("cargo test", command.Command);
        Assert.Equal(cwd, command.Cwd);
        Assert.Equal("tool", command.ToolCallId);
        Assert.Equal("terminal", command.TerminalId);
        Assert.Null(peer.Client.GetSessionSnapshot("one"));
        Assert.DoesNotContain(peer.Methods, static method => method.StartsWith("terminal/", StringComparison.Ordinal));
        Assert.Single(peer.Responses);
    }

    [Fact]
    public async Task PermissionRequest_ToolSubject_DoesNotOverwriteToolProjectionOrPromptText()
    {
        // Arrange
        using var peer = await PermissionPeer.CreateAsync();
        peer.Deliver("""{"jsonrpc":"2.0","method":"session/update","params":{"sessionId":"one","update":{"sessionUpdate":"tool_call_update","toolCallId":"tool","title":"existing title","content":[{"type":"content","content":{"type":"text","text":"existing content"}}]}}}""");

        // Act
        peer.Request("{\"sessionId\":\"one\",\"title\":\"Permission title\",\"description\":\"Permission explanation\","
            + "\"subject\":{\"type\":\"tool_call\",\"toolCall\":{\"toolCallId\":\"tool\",\"title\":\"subject title\",\"content\":[]},\"future\":1e2},"
            + Options + "}");
        var draft = Assert.Single(peer.Requests).GetDraftRequest()!;

        // Assert
        Assert.Equal("Permission title", draft.Title);
        Assert.Equal("Permission explanation", draft.Description);
        var subject = Assert.IsType<ToolCallPermissionSubject>(draft.Subject);
        Assert.Equal("subject title", subject.ToolCall.Title);
        Assert.Equal("1e2", subject.ExtensionData!["future"].GetRawText());
        var tool = Assert.Single(peer.Client.GetSessionSnapshot("one")!.ToolCalls);
        Assert.Equal("existing title", tool.Title);
        Assert.Single(tool.Content);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("_vendor")]
    [InlineData("future")]
    public async Task PermissionRequest_UnknownSubject_PreservesPayloadAndUsesGenericPrompt(string type)
    {
        // Arrange
        using var peer = await PermissionPeer.CreateAsync();
        var raw = "{\"type\":\"" + type + "\", \"value\":1e2,\"_meta\":17,\"command\":\"must not run\"}";

        // Act
        peer.Request("{\"sessionId\":\"one\",\"title\":\"Review\",\"subject\":" + raw + "," + Options + "}");
        var request = Assert.Single(peer.Requests);
        await request.Respond("cancelled", null);

        // Assert
        var subject = Assert.IsType<CustomRequestPermissionSubject>(request.GetDraftRequest()!.Subject);
        Assert.Equal(raw, subject.RawPayload.GetRawText());
        var forwarded = Json(JsonSerializer.Serialize<RequestPermissionSubject>(subject,
            AcpWireFormat.For(AcpProtocolVersion.V2).TypeInfo<RequestPermissionSubject>()));
        Assert.True(JsonElement.DeepEquals(subject.RawPayload, forwarded));
        Assert.Equal("1e2", forwarded.GetProperty("value").GetRawText());
        Assert.Null(request.ToolCall);
        Assert.Null(peer.Client.GetSessionSnapshot("one"));
        Assert.Equal("cancelled", Outcome(Assert.Single(peer.Responses)));
    }

    [Fact]
    public void ReadRequest_DefaultableSubjectFieldsAndOptions_KeepRequiredContractStrict()
    {
        // Arrange
        var raw = Json("""{"sessionId":"one","title":"Review","subject":{"type":"command","command":"echo","cwd":"/work","toolCallId":17,"terminalId":{},"_meta":"bad","future":1e2},"options":[{"optionId":"","name":"","kind":"future","_meta":[] }]}""");

        // Act
        var draft = AcpPermissionDraftExtensions.ReadRequest(raw);

        // Assert
        var command = Assert.IsType<CommandPermissionSubject>(draft.Subject);
        Assert.Null(command.ToolCallId);
        Assert.Null(command.TerminalId);
        Assert.Null(command.Meta);
        Assert.Equal("1e2", command.ExtensionData!["future"].GetRawText());
        var option = Assert.Single(draft.Options);
        Assert.Equal("future", option.Kind);
        Assert.Null(option.Meta);
    }

    [Fact]
    public void ReadRequest_ToolUpsertDefaultRecovery_UsesSameSchemaAsSessionUpdates()
    {
        // Arrange
        var raw = Json("{\"sessionId\":\"one\",\"title\":\"Review\",\"subject\":{\"type\":\"tool_call\",\"toolCall\":{\"toolCallId\":\"\",\"title\":1,\"content\":[27,{\"type\":\"terminal\",\"terminalId\":\"term\"}],\"_meta\":false}}," + Options + "}");

        // Act
        var draft = AcpPermissionDraftExtensions.ReadRequest(raw);

        // Assert
        var tool = Assert.IsType<ToolCallPermissionSubject>(draft.Subject).ToolCall;
        Assert.Equal("", tool.ToolCallId);
        Assert.Null(tool.Title);
        Assert.Single(tool.Content!);
        Assert.Null(tool.Meta);
    }

    [Fact]
    public void ReadRequest_MutatedDtoGetters_DoNotChangeSnapshotOrForwardedParameters()
    {
        // Arrange
        var raw = Json("{\"sessionId\":\"one\",\"title\":\"Review\",\"subject\":{\"type\":\"tool_call\",\"toolCall\":{\"toolCallId\":\"tool\",\"content\":[{\"type\":\"terminal\",\"terminalId\":\"term\"}]},\"future\":1e2},"
            + "\"options\":[{\"optionId\":\"allow\",\"name\":\"Allow\",\"kind\":\"allow_once\",\"_meta\":{\"a\":1}}]}");
        var draft = AcpPermissionDraftExtensions.ReadRequest(raw);

        // Act
        var subject = Assert.IsType<ToolCallPermissionSubject>(draft.Subject);
        subject.ToolCall.Content!.Clear();
        subject.ExtensionData!.Clear();
        draft.Options[0].Meta!.Clear();

        // Assert
        Assert.Single(Assert.IsType<ToolCallPermissionSubject>(draft.Subject).ToolCall.Content!);
        Assert.Single(Assert.IsType<ToolCallPermissionSubject>(draft.Subject).ExtensionData!);
        Assert.Single(draft.Options[0].Meta!);
        Assert.Equal(raw.GetRawText(), draft.RawParameters.GetRawText());
    }

    [Theory]
    [InlineData("{\"type\":\"command\",\"command\":\"echo\",\"cwd\":\"/work\",\"future\":1e2}")]
    [InlineData("{\"type\":\"tool_call\",\"toolCall\":{\"toolCallId\":\"tool\"},\"future\":1e2}")]
    public void ReadRequest_KnownSubjectWithExtensions_RoundTripsAndCannotOverrideItsDiscriminator(string json)
    {
        // Arrange
        var draft = AcpPermissionDraftExtensions.ReadRequest(Json("{\"sessionId\":\"one\",\"title\":\"Review\",\"subject\":" + json + "," + Options + "}"));
        var subject = draft.Subject!;
        var wire = AcpWireFormat.For(AcpProtocolVersion.V2).TypeInfo<RequestPermissionSubject>();

        // Act
        var forwarded = Json(JsonSerializer.Serialize(subject, wire));

        // Assert
        Assert.True(JsonElement.DeepEquals(Json(json), forwarded));
        Assert.Equal("1e2", forwarded.GetProperty("future").GetRawText());
        subject.ExtensionData!["type"] = Json("\"forged\"");
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(subject, wire));
        Assert.Equal(Json(json).GetProperty("type").GetString(), draft.Subject!.Type);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{\"title\":\"Review\",\"options\":[{\"optionId\":\"a\",\"name\":\"A\",\"kind\":\"allow_once\"}]}")]
    [InlineData("{\"sessionId\":\"one\",\"options\":[{\"optionId\":\"a\",\"name\":\"A\",\"kind\":\"allow_once\"}]}")]
    [InlineData("{\"sessionId\":\"one\",\"title\":null,\"options\":[{\"optionId\":\"a\",\"name\":\"A\",\"kind\":\"allow_once\"}]}")]
    [InlineData("{\"sessionId\":\"one\",\"title\":\"Review\",\"options\":[]}")]
    [InlineData("{\"sessionId\":\"one\",\"title\":\"Review\",\"options\":null}")]
    [InlineData("{\"sessionId\":\"one\",\"title\":\"Review\",\"options\":[null]}")]
    [InlineData("{\"sessionId\":\"one\",\"title\":\"Review\",\"options\":[{}]}")]
    [InlineData("{\"sessionId\":\"one\",\"title\":\"Review\",\"options\":[{\"optionId\":null,\"name\":\"A\",\"kind\":\"allow_once\"}]}")]
    public async Task PermissionRequest_InvalidRequiredEnvelope_RejectsWithoutPublishing(string json)
    {
        // Arrange
        using var peer = await PermissionPeer.CreateAsync();

        // Act
        peer.Request(json);

        // Assert
        Assert.Empty(peer.Requests);
        Assert.Equal(JsonRpcErrorCode.InvalidParams, Assert.Single(peer.Responses).Error!.Code);
        Assert.Empty(peer.Errors);
    }

    [Theory]
    [InlineData("false")]
    [InlineData("{}")]
    [InlineData("{\"type\":null}")]
    [InlineData("{\"type\":17}")]
    [InlineData("{\"type\":\"tool_call\"}")]
    [InlineData("{\"type\":\"tool_call\",\"toolCall\":{}}")]
    [InlineData("{\"type\":\"tool_call\",\"toolCall\":{\"toolCallId\":null}}")]
    [InlineData("{\"type\":\"command\",\"cwd\":\"/work\"}")]
    [InlineData("{\"type\":\"command\",\"command\":\"echo\"}")]
    [InlineData("{\"type\":\"command\",\"command\":null,\"cwd\":\"/work\"}")]
    [InlineData("{\"type\":\"command\",\"command\":\"echo\",\"cwd\":\"relative\"}")]
    public async Task PermissionRequest_InvalidProvidedSubject_RejectsInsteadOfTreatingItAsAbsent(string subject)
    {
        // Arrange
        using var peer = await PermissionPeer.CreateAsync();

        // Act
        peer.Request("{\"sessionId\":\"one\",\"title\":\"Review\",\"subject\":" + subject + "," + Options + "}");

        // Assert
        Assert.Empty(peer.Requests);
        Assert.Equal(JsonRpcErrorCode.InvalidParams, Assert.Single(peer.Responses).Error!.Code);
        Assert.Empty(peer.Errors);
    }

    [Fact]
    public async Task CancelSessionAsync_DraftPermission_CancelsWithoutWaitingForOptionalSubject()
    {
        // Arrange
        using var peer = await PermissionPeer.CreateAsync();
        peer.Request("{\"sessionId\":\"one\",\"title\":\"Review\"," + Options + "}");
        var request = Assert.Single(peer.Requests);

        // Act
        await peer.Client.CancelSessionAsync(new SessionCancelParams("one"), TestToken);
        await request.Respond("selected", "allow");

        // Assert
        Assert.Equal("cancelled", Outcome(Assert.Single(peer.Responses)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PermissionRequest_ReusedId_RejectsStaleDraftCallback(bool reconnect)
    {
        // Arrange
        using var peer = await PermissionPeer.CreateAsync();
        peer.Request("{\"sessionId\":\"old\",\"title\":\"Old prompt\"," + Options + "}");
        var old = Assert.Single(peer.Requests);
        if (reconnect)
        {
            await peer.Client.DisconnectAsync();
            await peer.InitializeAsync();
        }
        else
        {
            await old.Respond("cancelled", null);
        }
        peer.Request("{\"sessionId\":\"new\",\"title\":\"New prompt\"," + Options + "}");
        var count = peer.Responses.Count;

        // Act
        await old.Respond("selected", "allow");

        // Assert
        Assert.Equal(count, peer.Responses.Count);
        var current = peer.Requests[^1];
        Assert.Equal("New prompt", current.GetDraftRequest()!.Title);
        await current.Respond("selected", "allow");
        Assert.Equal(count + 1, peer.Responses.Count);
        Assert.Equal("selected", Outcome(peer.Responses[^1]));
        Assert.Empty(peer.Errors);
    }

    [Fact]
    public async Task PermissionRequest_ResponseDelayedAcrossReconnect_DoesNotWriteToNewConnection()
    {
        // Arrange
        using var peer = await PermissionPeer.CreateAsync();
        peer.Request("{\"sessionId\":\"old\",\"title\":\"Old prompt\"," + Options + "}");
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        peer.ResponseSend = (_, token) => release.Task.WaitAsync(token);
        var answer = peer.Requests[0].Respond("selected", "allow");
        Assert.Single(peer.Attempts);

        try
        {
            // Act
            await peer.Client.DisconnectAsync();
            await peer.InitializeAsync();
            peer.Request("{\"sessionId\":\"new\",\"title\":\"New prompt\"," + Options + "}");
            release.TrySetResult(true);
            await answer.WaitAsync(TestToken);

            // Assert
            Assert.Empty(peer.Responses);
            peer.ResponseSend = null;
            await peer.Requests[^1].Respond("selected", "allow");
            Assert.Equal("selected", Outcome(Assert.Single(peer.Responses)));
            Assert.Empty(peer.Errors);
        }
        finally
        {
            release.TrySetResult(true);
            await answer.WaitAsync(TestToken);
        }
    }

    [Fact]
    public async Task PermissionRequest_FailedSendAndInvalidChoice_KeepOriginalDraftRequestForRetry()
    {
        // Arrange
        using var peer = await PermissionPeer.CreateAsync();
        peer.Request("{\"sessionId\":\"one\",\"title\":\"Review\"," + Options + "}");
        var request = Assert.Single(peer.Requests);
        request.Options.Clear();
        request.Options.Add(new PermissionOption("forged", "Forged", "allow_always"));

        // Act / Assert
        await Assert.ThrowsAsync<AcpException>(() => request.Respond("selected", "forged"));
        Assert.Empty(peer.Attempts);
        peer.ResponseSend = (_, _) => Task.FromResult(false);
        await request.Respond("selected", "allow");
        Assert.Empty(peer.Responses);
        peer.ResponseSend = null;
        await request.Respond("selected", "allow");
        Assert.Equal("allow", Assert.Single(peer.Responses).Result!.Value.GetProperty("outcome").GetProperty("optionId").GetString());
        Assert.Equal("allow", Assert.Single(request.GetDraftRequest()!.Options).OptionId);
    }

    [Fact]
    public async Task PermissionRequest_NoSubscriber_CancelsOnce()
    {
        // Arrange
        using var peer = await PermissionPeer.CreateAsync(subscribe: false);

        // Act
        peer.Request("{\"sessionId\":\"one\",\"title\":\"Review\"," + Options + "}");

        // Assert
        Assert.Equal("cancelled", Outcome(Assert.Single(peer.Responses)));
        Assert.False(await peer.Client.RespondToPermissionRequestAsync(81L, "selected", "allow"));
        Assert.Empty(peer.Errors);
    }

    [Fact]
    public async Task PermissionRequest_SubscriberJsonException_ReportsClientFailureWithoutBlamingAgentPayload()
    {
        // Arrange
        using var peer = await PermissionPeer.CreateAsync();
        peer.Client.PermissionRequestReceived += (_, _) => throw new JsonException("Host display failed.");

        // Act
        peer.Request("{\"sessionId\":\"one\",\"title\":\"Valid prompt\"," + Options + "}");

        // Assert
        Assert.Equal(JsonRpcErrorCode.InternalError, Assert.Single(peer.Responses).Error!.Code);
        Assert.Contains("Host display failed.", Assert.Single(peer.Errors), StringComparison.Ordinal);
        await Assert.Single(peer.Requests).Respond("selected", "allow");
        Assert.Single(peer.Responses);
    }

    [Fact]
    public async Task PermissionRequest_SubscriberThrowsAfterAnswer_DoesNotSendASecondResponse()
    {
        // Arrange
        using var peer = await PermissionPeer.CreateAsync();
        Task? answer = null;
        peer.Client.PermissionRequestReceived += (_, request) =>
        {
            answer = request.Respond("selected", "allow");
            throw new InvalidOperationException("Host failed after answering.");
        };

        // Act
        peer.Request("{\"sessionId\":\"one\",\"title\":\"Valid prompt\"," + Options + "}");
        Assert.NotNull(answer);
        await answer.WaitAsync(TestToken);

        // Assert
        Assert.Equal("selected", Outcome(Assert.Single(peer.Responses)));
        Assert.Contains("Host failed after answering.", Assert.Single(peer.Errors), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PermissionRequest_V1ToolCall_RemainsStableAndHasNoDraftAccessor()
    {
        // Arrange
        using var peer = await PermissionPeer.CreateAsync(AcpProtocolVersion.V1);

        // Act
        peer.Request("{\"sessionId\":\"one\",\"title\":\"Ignored v2 field\",\"toolCall\":{\"toolCallId\":\"tool\"}," + Options + "}");

        // Assert
        var request = Assert.Single(peer.Requests);
        Assert.NotNull(request.ToolCall);
        Assert.Null(request.GetDraftRequest());
    }

    [Fact]
    public async Task PermissionRequest_V1WithoutToolCall_DoesNotSilentlyAdoptV2()
    {
        // Arrange
        using var peer = await PermissionPeer.CreateAsync(AcpProtocolVersion.V1);

        // Act
        peer.Request("{\"sessionId\":\"one\",\"title\":\"Review\"," + Options + "}");

        // Assert
        Assert.Empty(peer.Requests);
        Assert.Equal(JsonRpcErrorCode.InvalidParams, Assert.Single(peer.Responses).Error!.Code);
    }

    [Fact]
    public void ReadRequest_ArbitraryPromptAndCustomSubject_PreservesStringsAndRawFields()
        => FsCheckPropertyRunner.Run(this, nameof(PermissionPromptRoundTripProperty));

    private void PermissionPromptRoundTripProperty(string? sessionId, string? title, string? description, string? subjectType)
    {
        // Arrange
        var type = "_" + subjectType;
        var raw = Json("{\"sessionId\":" + Quoted(sessionId ?? string.Empty) + ",\"title\":" + Quoted(title ?? string.Empty)
            + ",\"description\":" + Quoted(description) + ",\"subject\":{\"type\":" + Quoted(type)
            + ",\"future\":{\"value\":1e2}}," + Options + "}");

        // Act
        var draft = AcpPermissionDraftExtensions.ReadRequest(raw);
        var subject = Assert.IsType<CustomRequestPermissionSubject>(draft.Subject);
        var forwarded = Json(JsonSerializer.Serialize<RequestPermissionSubject>(subject,
            AcpWireFormat.For(AcpProtocolVersion.V2).TypeInfo<RequestPermissionSubject>()));

        // Assert
        Assert.Equal(sessionId ?? string.Empty, draft.SessionId);
        Assert.Equal(title ?? string.Empty, draft.Title);
        Assert.Equal(description, draft.Description);
        Assert.Equal(type, subject.Type);
        Assert.Equal(raw.GetRawText(), draft.RawParameters.GetRawText());
        Assert.True(JsonElement.DeepEquals(raw.GetProperty("subject"), forwarded));
        Assert.Equal("1e2", forwarded.GetProperty("future").GetProperty("value").GetRawText());
    }

    private static string Quoted(string? value)
        => value is null ? "null" : JsonSerializer.Serialize(value, AcpJsonContext.Default.String);

    private static string? Outcome(JsonRpcResponse response)
        => response.Result!.Value.GetProperty("outcome").GetProperty("outcome").GetString();

    private static JsonElement Json(string text)
    {
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    private sealed class PermissionPeer : IAcpTransport
    {
        private readonly MessageParser _parser = new();
        private readonly int _version;

        private PermissionPeer(int version, bool subscribe)
        {
            _version = version;
            Client = new AcpClient(this);
            if (subscribe) Client.PermissionRequestReceived += (_, request) => Requests.Add(request);
            Client.ErrorOccurred += (_, error) => Errors.Add(error);
        }

        public bool IsConnected { get; private set; }
        internal AcpClient Client { get; }
        internal List<PermissionRequestEventArgs> Requests { get; } = [];
        internal List<JsonRpcResponse> Responses { get; } = [];
        internal List<JsonRpcResponse> Attempts { get; } = [];
        internal List<string> Methods { get; } = [];
        internal List<string> Errors { get; } = [];
        internal Func<JsonRpcResponse, CancellationToken, Task<bool>>? ResponseSend { get; set; }

        public event EventHandler<AcpTransportMessageReceivedEventArgs>? MessageReceived;
        public event EventHandler<AcpTransportErrorEventArgs>? ErrorOccurred { add { } remove { } }

        internal static async Task<PermissionPeer> CreateAsync(int version = AcpProtocolVersion.V2, bool subscribe = true)
        {
            var peer = new PermissionPeer(version, subscribe);
            await peer.InitializeAsync();
            return peer;
        }

        internal Task<InitializeResponse> InitializeAsync()
        {
            var request = new InitializeParams(new ClientInfo("draft-permission", "1.0"), new ClientCapabilities()) { ProtocolVersion = _version };
            return _version == AcpProtocolVersion.V2
                ? Client.InitializeDraftAsync(request, TestToken) : Client.InitializeAsync(request, TestToken);
        }

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
                    var result = JsonSerializer.Serialize(new InitializeResponse(_version, new AgentInfo("permission-agent", "1.0"),
                        new AgentCapabilities()), AcpWireFormat.For(_version).TypeInfo<InitializeResponse>());
                    Deliver("{\"jsonrpc\":\"2.0\",\"id\":" + request.Id + ",\"result\":" + result + "}");
                    Methods.Add(request.Method);
                    break;
                case JsonRpcRequest request:
                    Methods.Add(request.Method);
                    break;
                case JsonRpcNotification notification:
                    Methods.Add(notification.Method);
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

        internal void Request(string parameters)
            => Deliver("{\"jsonrpc\":\"2.0\",\"id\":81,\"method\":\"session/request_permission\",\"params\":" + parameters + "}");

        internal void Deliver(string message) => MessageReceived?.Invoke(this, new AcpTransportMessageReceivedEventArgs(message));

        public void Dispose()
        {
            Client.Dispose();
            IsConnected = false;
        }
    }
}
