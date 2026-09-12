using System.Text.Json;
using SalmonEgg.Acp.Client;
using SalmonEgg.Acp.JsonRpc;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Acp.Serialization;

namespace SalmonEgg.Acp.Tests.Client;

public sealed class AcpSessionConfigurationTests
{
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;
    private const string InitialOptions = """[{"configId":"model","name":"Model","type":"select","currentValue":"fast","options":[{"value":"fast","name":"Fast"}]},{"configId":"reasoning","name":"Reasoning","type":"boolean","currentValue":false}]""";

    [Fact]
    public async Task CreateSession_ConfigurationIsCommittedBeforeAsyncStorageAndLaterUpdates()
    {
        var storage = new DelayedSessionStore();
        using var peer = await ConfigurationPeer.CreateAsync(storage);
        var created = peer.Client.CreateSessionAsync(new SessionNewParams("/workspace", []), TestToken);
        await storage.Entered.Task.WaitAsync(TestToken);

        var initial = peer.Client.GetSessionSnapshot("one");
        peer.Update(BooleanOptions(true));
        storage.Release.TrySetResult();
        await created;

        Assert.NotNull(initial);
        Assert.True(initial.HasConfigOptions);
        Assert.Equal(["model", "reasoning"], initial.ConfigOptions.Select(static option => option.Id));
        Assert.False(initial.ConfigOptions[1].CurrentBooleanValue);
        Assert.True(Assert.Single(peer.Client.GetSessionSnapshot("one")!.ConfigOptions).CurrentBooleanValue);
    }

    [Fact]
    public async Task SetOption_ResponseAndNotificationFollowReceiveOrder()
    {
        using var peer = await ConfigurationPeer.CreateWithSessionAsync();
        peer.OnSet = request =>
        {
            peer.Reply(request, OptionsResponse(BooleanOptions(true)));
            peer.Update(BooleanOptions(false));
        };

        var result = await peer.Client.SetSessionConfigOptionAsync(new("one", "reasoning", true), TestToken);

        Assert.True(Assert.Single(result.ConfigOptions!).CurrentBooleanValue);
        Assert.False(Assert.Single(peer.Client.GetSessionSnapshot("one")!.ConfigOptions).CurrentBooleanValue);
        Assert.Empty(peer.Errors);
    }

    [Fact]
    public async Task SetOption_ConcurrentRequests_ResponseOrderOwnsFullState()
    {
        using var peer = await ConfigurationPeer.CreateWithSessionAsync();
        var first = peer.Client.SetSessionConfigOptionAsync(new("one", "reasoning", true), TestToken);
        var firstRequest = peer.LastSet!;
        var second = peer.Client.SetSessionConfigOptionAsync(new("one", "reasoning", false), TestToken);

        peer.Reply(peer.LastSet!, OptionsResponse(BooleanOptions(false)));
        await second;
        peer.Reply(firstRequest, OptionsResponse(BooleanOptions(true)));
        await first;

        Assert.True(Assert.Single(peer.Client.GetSessionSnapshot("one")!.ConfigOptions).CurrentBooleanValue);
    }

    [Theory]
    [InlineData("id", "\"fast\"")]
    [InlineData("boolean", "true")]
    public async Task SetOption_UsesTypedWireAndFullAuthoritativeResponse(string type, string value)
    {
        using var peer = await ConfigurationPeer.CreateWithSessionAsync();
        peer.OnSet = request => peer.Reply(request, OptionsResponse(BooleanOptions(true)));
        var request = type == "id"
            ? new SessionSetConfigOptionParams("one", "model", "fast")
            : new SessionSetConfigOptionParams("one", "reasoning", true);

        await peer.Client.SetSessionConfigOptionAsync(request, TestToken);

        Assert.Equal(type, peer.LastSet!.Params!.Value.GetProperty("type").GetString());
        Assert.Equal(value, peer.LastSet.Params.Value.GetProperty("value").GetRawText());
        Assert.Equal("reasoning", Assert.Single(peer.Client.GetSessionSnapshot("one")!.ConfigOptions).Id);
    }

    [Fact]
    public async Task SetOption_AbandonedAwait_StillCommitsThePeersFinalResponse()
    {
        using var peer = await ConfigurationPeer.CreateWithSessionAsync();
        using var caller = new CancellationTokenSource();
        var pending = peer.Client.SetSessionConfigOptionAsync(new("one", "reasoning", true), caller.Token);
        caller.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);

        peer.Reply(peer.LastSet!, OptionsResponse(BooleanOptions(true)));

        Assert.True(Assert.Single(peer.Client.GetSessionSnapshot("one")!.ConfigOptions).CurrentBooleanValue);
    }

    [Fact]
    public async Task SetOption_PeerError_DoesNotOptimisticallyChangeConfiguration()
    {
        using var peer = await ConfigurationPeer.CreateWithSessionAsync();
        var pending = peer.Client.SetSessionConfigOptionAsync(new("one", "reasoning", true), TestToken);
        peer.Error(peer.LastSet!, JsonRpcErrorCode.InvalidParams);

        await Assert.ThrowsAsync<AcpException>(() => pending);

        Assert.False(peer.Client.GetSessionSnapshot("one")!.ConfigOptions[1].CurrentBooleanValue);
    }

    [Fact]
    public async Task SetOption_InvalidResponse_FaultsTheRequestAndPreservesLastState()
    {
        using var peer = await ConfigurationPeer.CreateWithSessionAsync();
        peer.OnSet = request => peer.Reply(request, "{}");

        var failure = await Record.ExceptionAsync(async () => await peer.Client.SetSessionConfigOptionAsync(
            new("one", "reasoning", true), TestToken).WaitAsync(TimeSpan.FromSeconds(5), TestToken));

        Assert.IsType<JsonException>(failure);
        Assert.False(peer.Client.GetSessionSnapshot("one")!.ConfigOptions[1].CurrentBooleanValue);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResumeSession_MissingOptionalList_IsAnAuthoritativeEmptyList(bool replay)
    {
        using var peer = await ConfigurationPeer.CreateWithSessionAsync();

        await peer.Client.ResumeSessionAsync(new SessionResumeParams("one", "/workspace", [],
            replayFrom: replay ? SessionReplayFrom.Start : null), TestToken);

        var snapshot = peer.Client.GetSessionSnapshot("one")!;
        Assert.True(snapshot.HasConfigOptions);
        Assert.Empty(snapshot.ConfigOptions);
    }

    [Fact]
    public async Task ClosedSession_IgnoresLateConfigurationResponse()
    {
        using var peer = await ConfigurationPeer.CreateWithSessionAsync();
        var pending = peer.Client.SetSessionConfigOptionAsync(new("one", "reasoning", true), TestToken);
        await peer.Client.CloseSessionAsync(new("one"), TestToken);

        peer.Reply(peer.LastSet!, OptionsResponse(BooleanOptions(true)));
        await pending;

        Assert.Null(peer.Client.GetSessionSnapshot("one"));
    }

    [Fact]
    public async Task ReconnectedSession_DoesNotReceiveOldConfigurationResponse()
    {
        using var peer = await ConfigurationPeer.CreateWithSessionAsync();
        var pending = peer.Client.SetSessionConfigOptionAsync(new("one", "reasoning", true), TestToken);
        var deliverOld = peer.CaptureDelivery();
        var oldResponse = peer.Response(peer.LastSet!, OptionsResponse(BooleanOptions(true)));
        await peer.Client.DisconnectAsync();
        await Assert.ThrowsAnyAsync<Exception>(() => pending);
        await peer.InitializeAsync();
        await peer.Client.CreateSessionAsync(new SessionNewParams("/workspace", []), TestToken);

        deliverOld(oldResponse);

        Assert.False(peer.Client.GetSessionSnapshot("one")!.ConfigOptions[1].CurrentBooleanValue);
    }

    [Fact]
    public async Task V1_SetOption_KeepsStableWireAndDoesNotExposeDraftState()
    {
        using var peer = await ConfigurationPeer.CreateAsync(version: AcpProtocolVersion.V1);
        await peer.Client.CreateSessionAsync(new SessionNewParams("/workspace", []), TestToken);
        peer.OnSet = request => peer.Reply(request, "{\"configOptions\":[]}");

        await peer.Client.SetSessionConfigOptionAsync(new("one", "model", "fast"), TestToken);

        Assert.False(peer.LastSet!.Params!.Value.TryGetProperty("type", out _));
        Assert.Null(peer.Client.GetSessionSnapshot("one"));
    }

    [Fact]
    public void OfflineReplay_ConfigurationPreservesUnknownOptionsAndDetachedPriorityOrder()
    {
        const string options = """[{"configId":"custom","name":"Custom","type":"_budget","currentValue":{"tokens":1e2},"future":true},{"configId":"reasoning","name":"Reasoning","type":"boolean","currentValue":true},{"configId":"model","name":"Model","type":"select","currentValue":"fast","options":[{"groupId":"g","name":"Group","options":[{"value":"fast","name":"Fast","_meta":{"hint":"owned"}}]}]}]""";

        var snapshot = AcpSessionDraftExtensions.ReplaySession("one", [Json(Update(options))]);
        var copy = snapshot.ConfigOptions;
        copy[2].OptionGroups[0].Options.Clear();

        Assert.True(snapshot.HasConfigOptions);
        Assert.Equal(["custom", "reasoning", "model"], snapshot.ConfigOptions.Select(static option => option.Id));
        var raw = JsonSerializer.SerializeToElement(snapshot.ConfigOptions[0], AcpWireFormat.For(2).TypeInfo<ConfigOption>());
        Assert.Equal("1e2", raw.GetProperty("currentValue").GetProperty("tokens").GetRawText());
        Assert.True(raw.GetProperty("future").GetBoolean());
        Assert.Single(snapshot.ConfigOptions[2].OptionGroups[0].Options);
        Assert.Empty(snapshot.UnprojectedUpdates);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("false")]
    public void OfflineReplay_FullConfigurationReplacement_ClearsOldOptions(string replacement)
    {
        var snapshot = AcpSessionDraftExtensions.ReplaySession("one", [Json(Update(InitialOptions)), Json(Update(replacement))]);

        Assert.True(snapshot.HasConfigOptions);
        Assert.Empty(snapshot.ConfigOptions);
    }

    [Fact]
    public void OfflineReplay_NoConfigUpdate_DoesNotInventAuthoritativeState()
    {
        var snapshot = AcpSessionDraftExtensions.ReplaySession("one", []);

        Assert.False(snapshot.HasConfigOptions);
        Assert.Empty(snapshot.ConfigOptions);
    }

    private static string BooleanOptions(bool value)
        => "[{\"configId\":\"reasoning\",\"name\":\"Reasoning\",\"type\":\"boolean\",\"currentValue\":"
            + (value ? "true" : "false") + "}]";

    private static string OptionsResponse(string options) => "{\"configOptions\":" + options + "}";
    private static string Update(string options) => "{\"sessionUpdate\":\"config_option_update\",\"configOptions\":" + options + "}";
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private sealed class DelayedSessionStore : IAcpClientSessionStore
    {
        private readonly InMemoryAcpClientSessionStore _inner = new();
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool ContainsSession(string sessionId) => _inner.ContainsSession(sessionId);
        public bool RemoveSession(string sessionId) => _inner.RemoveSession(sessionId);
        public bool UpdateCurrentMode(string sessionId, string modeId) => _inner.UpdateCurrentMode(sessionId, modeId);
        public Task<bool> CancelSessionAsync(string sessionId) => _inner.CancelSessionAsync(sessionId);
        public async Task CreateSessionAsync(string sessionId, string cwd)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(TestToken);
            await _inner.CreateSessionAsync(sessionId, cwd);
        }
    }

    private sealed class ConfigurationPeer : IAcpTransport, IDisposable
    {
        private readonly int _version;
        private readonly MessageParser _parser = new();
        internal AcpClient Client { get; }
        internal List<string> Errors { get; } = [];
        internal JsonRpcRequest? LastSet { get; private set; }
        internal Action<JsonRpcRequest>? OnSet { get; set; }
        public bool IsConnected { get; private set; } = true;
        public event EventHandler<AcpTransportMessageReceivedEventArgs>? MessageReceived;
        public event EventHandler<AcpTransportErrorEventArgs>? ErrorOccurred { add { } remove { } }

        private ConfigurationPeer(IAcpClientSessionStore? store, int version)
        {
            _version = version;
            Client = new AcpClient(this, sessionStore: store);
            Client.ErrorOccurred += (_, error) => Errors.Add(error);
        }

        internal static async Task<ConfigurationPeer> CreateAsync(IAcpClientSessionStore? store = null, int version = AcpProtocolVersion.V2)
        {
            var peer = new ConfigurationPeer(store, version);
            await peer.InitializeAsync();
            return peer;
        }

        internal static async Task<ConfigurationPeer> CreateWithSessionAsync()
        {
            var peer = await CreateAsync();
            await peer.Client.CreateSessionAsync(new SessionNewParams("/workspace", []), TestToken);
            return peer;
        }

        internal Task<InitializeResponse> InitializeAsync()
        {
            var request = new InitializeParams(new ClientInfo("config-test", "1"), new ClientCapabilities()) { ProtocolVersion = _version };
            return _version == AcpProtocolVersion.V2
                ? Client.InitializeDraftAsync(request, TestToken)
                : Client.InitializeAsync(request, TestToken);
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

        public Task<bool> SendMessageAsync(string json, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_parser.ParseMessage(json) is not JsonRpcRequest request) return Task.FromResult(true);
            switch (request.Method)
            {
                case "initialize":
                    var initialize = new InitializeResponse(_version, new AgentInfo("config-peer", "1"), new AgentCapabilities
                    {
                        SessionCapabilities = new SessionCapabilities { Resume = new SessionResumeCapabilities(), Close = new SessionCloseCapabilities() }
                    });
                    Reply(request, JsonSerializer.Serialize(initialize, AcpWireFormat.For(_version).TypeInfo<InitializeResponse>()));
                    break;
                case "session/new":
                    Reply(request, _version == AcpProtocolVersion.V2
                        ? "{\"sessionId\":\"one\",\"configOptions\":" + InitialOptions + "}"
                        : "{\"sessionId\":\"one\"}");
                    break;
                case "session/resume":
                case "session/close":
                    Reply(request, "{}");
                    break;
                case "session/set_config_option":
                    LastSet = request;
                    OnSet?.Invoke(request);
                    break;
            }
            return Task.FromResult(true);
        }

        internal void Update(string options) => Deliver("{\"jsonrpc\":\"2.0\",\"method\":\"session/update\",\"params\":{\"sessionId\":\"one\",\"update\":" + AcpSessionConfigurationTests.Update(options) + "}}");
        internal void Reply(JsonRpcRequest request, string result) => Deliver(Response(request, result));
        internal string Response(JsonRpcRequest request, string result) => "{\"jsonrpc\":\"2.0\",\"id\":" + request.Id + ",\"result\":" + result + "}";
        internal void Error(JsonRpcRequest request, int code) => Deliver("{\"jsonrpc\":\"2.0\",\"id\":" + request.Id + ",\"error\":{\"code\":" + code + ",\"message\":\"rejected\"}}");
        private void Deliver(string json) => MessageReceived?.Invoke(this, new AcpTransportMessageReceivedEventArgs(json));
        internal Action<string> CaptureDelivery()
        {
            var handlers = MessageReceived;
            return json => handlers?.Invoke(this, new AcpTransportMessageReceivedEventArgs(json));
        }

        public void Dispose() => Client.Dispose();
    }
}
