using System.Collections.Immutable;
using System.Text.Json;
using Moq;
using SalmonEgg.Acp.Client;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Acp.Serialization;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Core.Tests.Threading;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public partial class ChatViewModelTests
{
    [Fact]
    public async Task VersionedUpdates_MessageReplacementAndChunks_ReachTheSameProductMessage()
    {
        var dispatcher = new QueueingSynchronizationContext();
        await using var fixture = CreateInteractionViewModel(dispatcher);
        using var stablePeer = await PermissionUiPeer.CreateAsync();
        using var peer = await VersionedUpdatePeer.CreateAsync();
        await AttachPermissionPeerAsync(fixture, dispatcher, stablePeer, peer.Service);

        peer.Update("""{"sessionUpdate":"agent_message_chunk","messageId":"a","content":{"type":"text","text":"old"}}""");
        peer.Update("""{"sessionUpdate":"agent_message","messageId":"a","content":[{"type":"text","text":"new"},{"type":"resource_link","uri":"https://example.test/document","name":"Document"}]}""");
        peer.Update("""{"sessionUpdate":"agent_message_chunk","messageId":"a","content":{"type":"text","text":" tail"}}""");
        await DrainVersionedUpdatesAsync(fixture, dispatcher);

        var messages = (await fixture.ChatStore.GetCurrentStateAsync()).ResolveContentSlice("conv-1")!.Value.Transcript;
        Assert.Equal(3, messages.Count);
        Assert.Equal(["new", "https://example.test/document", " tail"], messages.Select(static message => message.TextContent));
        Assert.All(messages, static message => Assert.Equal("a", message.ProtocolMessageId));

        peer.Update("""{"sessionUpdate":"agent_message","messageId":"a","content":[]}""");
        await DrainVersionedUpdatesAsync(fixture, dispatcher);
        Assert.Empty((await fixture.ChatStore.GetCurrentStateAsync()).ResolveContentSlice("conv-1")!.Value.Transcript);
    }

    [Fact]
    public async Task VersionedUpdates_ToolReplacement_ClearsOmittedProjectionFieldsAndStreamsContent()
    {
        var dispatcher = new QueueingSynchronizationContext();
        await using var fixture = CreateInteractionViewModel(dispatcher);
        using var stablePeer = await PermissionUiPeer.CreateAsync();
        using var peer = await VersionedUpdatePeer.CreateAsync();
        await AttachPermissionPeerAsync(fixture, dispatcher, stablePeer, peer.Service);

        peer.Update("""{"sessionUpdate":"tool_call_update","toolCallId":"t","title":"Run","status":"in_progress","rawInput":{"x":1},"content":[{"type":"content","content":{"type":"text","text":"old"}}]}""");
        peer.Update("""{"sessionUpdate":"tool_call_update","toolCallId":"t","title":null,"rawInput":null,"status":"completed","content":[]}""");
        peer.Update("""{"sessionUpdate":"tool_call_content_chunk","toolCallId":"t","content":{"type":"content","content":{"type":"text","text":"result"}}}""");
        await DrainVersionedUpdatesAsync(fixture, dispatcher);

        var tool = Assert.Single((await fixture.ChatStore.GetCurrentStateAsync()).ResolveContentSlice("conv-1")!.Value.Transcript);
        Assert.Equal("t", tool.ToolCallId);
        Assert.Equal("completed", tool.ToolCallStatus);
        Assert.Empty(tool.Title);
        Assert.Null(tool.ToolCallRawInputJson);
        Assert.Contains("result", tool.TextContent);
    }

    [Fact]
    public async Task VersionedUpdates_TerminalBytesAndExit_UseDisplayOnlyPanel()
    {
        var dispatcher = new QueueingSynchronizationContext();
        await using var fixture = CreateInteractionViewModel(dispatcher);
        using var stablePeer = await PermissionUiPeer.CreateAsync();
        using var peer = await VersionedUpdatePeer.CreateAsync();
        await AttachPermissionPeerAsync(fixture, dispatcher, stablePeer, peer.Service);

        peer.Update("""{"sessionUpdate":"terminal_update","terminalId":"terminal","command":"echo","output":{"data":"4g=="}}""");
        peer.Update("""{"sessionUpdate":"terminal_output_chunk","terminalId":"terminal","data":"gqw="}""");
        peer.Update("""{"sessionUpdate":"terminal_update","terminalId":"terminal","exitStatus":{"exitCode":0}}""");
        await DrainVersionedUpdatesAsync(fixture, dispatcher);

        var terminal = Assert.Single(fixture.ViewModel.TerminalSessions);
        Assert.Equal("terminal", terminal.TerminalId);
        Assert.Equal("€", terminal.Output);
        Assert.Equal((uint)0, terminal.ExitCode);
        Assert.True(terminal.IsReleased);
        Assert.DoesNotContain(peer.SentMethods, static method => method.StartsWith("terminal/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task VersionedUpdates_RunningRequiresActionAndUnspecifiedIdle_CompleteTheProductTurn()
    {
        var dispatcher = new QueueingSynchronizationContext();
        await using var fixture = CreateInteractionViewModel(dispatcher);
        using var stablePeer = await PermissionUiPeer.CreateAsync();
        using var peer = await VersionedUpdatePeer.CreateAsync();
        await AttachPermissionPeerAsync(fixture, dispatcher, stablePeer, peer.Service);

        peer.Update("""{"sessionUpdate":"state_update","state":"running"}""");
        await DrainVersionedUpdatesAsync(fixture, dispatcher);
        Assert.Equal(ChatTurnPhase.WaitingForAgent, (await fixture.ChatStore.GetCurrentStateAsync()).ActiveTurn!.Phase);
        peer.Update("""{"sessionUpdate":"state_update","state":"requires_action"}""");
        peer.Update("""{"sessionUpdate":"state_update","state":"idle"}""");
        await DrainVersionedUpdatesAsync(fixture, dispatcher);

        Assert.Equal(ChatTurnPhase.Completed, (await fixture.ChatStore.GetCurrentStateAsync()).ActiveTurn!.Phase);
    }

    [Fact]
    public async Task VersionedUpdates_UserEcho_ReplacesTheOptimisticMessageWithoutDuplication()
    {
        var dispatcher = new QueueingSynchronizationContext();
        await using var fixture = CreateInteractionViewModel(dispatcher);
        using var stablePeer = await PermissionUiPeer.CreateAsync();
        using var peer = await VersionedUpdatePeer.CreateAsync();
        await AttachPermissionPeerAsync(fixture, dispatcher, stablePeer, peer.Service);
        var timestamp = DateTime.UtcNow;
        await fixture.DispatchAsync(new UpsertTranscriptMessageAction("conv-1",
            new ConversationMessageSnapshot { Id = "local", IsOutgoing = true, TextContent = "question", ContentType = "text", Timestamp = timestamp }));
        await fixture.DispatchAsync(new BeginTurnAction("conv-1", "turn", ChatTurnPhase.WaitingForAgent,
            PendingUserMessageLocalId: "local", PendingUserMessageText: "question"));

        peer.Update("""{"sessionUpdate":"user_message","messageId":"user-1","content":[{"type":"text","text":"question"}]}""");
        await DrainVersionedUpdatesAsync(fixture, dispatcher);

        var user = Assert.Single((await fixture.ChatStore.GetCurrentStateAsync()).ResolveContentSlice("conv-1")!.Value.Transcript);
        Assert.Equal("local", user.Id);
        Assert.Equal("user-1", user.ProtocolMessageId);
        Assert.Equal(timestamp, user.Timestamp);
    }

    [Fact]
    public async Task VersionedUpdates_PlanAndBooleanConfiguration_ReachTheProductStore()
    {
        var dispatcher = new QueueingSynchronizationContext();
        await using var fixture = CreateInteractionViewModel(dispatcher);
        using var stablePeer = await PermissionUiPeer.CreateAsync();
        using var peer = await VersionedUpdatePeer.CreateAsync();
        await AttachPermissionPeerAsync(fixture, dispatcher, stablePeer, peer.Service);

        peer.Update("""{"sessionUpdate":"plan_update","plan":{"type":"items","planId":"p","entries":[{"content":"Step","priority":"medium","status":"pending"}]}}""");
        peer.Update("""{"sessionUpdate":"config_option_update","configOptions":[{"configId":"auto","name":"Automatic","type":"boolean","currentValue":true}]}""");
        await DrainVersionedUpdatesAsync(fixture, dispatcher);

        var state = await fixture.ChatStore.GetCurrentStateAsync();
        Assert.Equal("Step", Assert.Single(state.ResolveContentSlice("conv-1")!.Value.PlanEntries).Content);
        Assert.True(Assert.Single(state.ResolveSessionStateSlice("conv-1")!.Value.ConfigOptions).BooleanValue);
    }

    [Fact]
    public async Task VersionedUpdates_ConfigurationResponse_LaterNotificationWinsInTheActualProductQueue()
    {
        var dispatcher = new QueueingSynchronizationContext();
        await using var fixture = CreateInteractionViewModel(dispatcher);
        using var stablePeer = await PermissionUiPeer.CreateAsync();
        using var peer = await VersionedUpdatePeer.CreateAsync();
        await AttachPermissionPeerAsync(fixture, dispatcher, stablePeer, peer.Service);
        peer.AfterConfigResponse = () => peer.Update("""{"sessionUpdate":"config_option_update","configOptions":[{"configId":"auto","name":"Automatic","type":"boolean","currentValue":false}]}""");

        var result = await peer.Service.SetSessionConfigOptionAsync(new("remote-1", "auto", true));
        await DrainVersionedUpdatesAsync(fixture, dispatcher);

        Assert.True(Assert.Single(result.ConfigOptions!).CurrentBooleanValue);
        Assert.False(Assert.Single((await fixture.ChatStore.GetCurrentStateAsync()).ResolveSessionStateSlice("conv-1")!.Value.ConfigOptions).BooleanValue);
    }

    [Fact]
    public async Task VersionedUpdates_CapturedOldConnectionUpdate_CannotAffectReplacementConversation()
    {
        var dispatcher = new QueueingSynchronizationContext();
        await using var fixture = CreateInteractionViewModel(dispatcher);
        using var stablePeer = await PermissionUiPeer.CreateAsync();
        using var peer = await VersionedUpdatePeer.CreateAsync();
        await AttachPermissionPeerAsync(fixture, dispatcher, stablePeer, peer.Service);
        SessionUpdateEventArgs? captured = null;
        peer.Service.SessionUpdateReceived += (_, update) => captured = update;
        peer.Update("""{"sessionUpdate":"agent_message","messageId":"old","content":[{"type":"text","text":"old context"}]}""");
        await DrainVersionedUpdatesAsync(fixture, dispatcher);
        Assert.NotNull(captured);
        await peer.DisconnectClientAsync();
        await AwaitWithSynchronizationContextAsync(dispatcher,
            fixture.ViewModel.ReplaceChatServiceAsync(stablePeer.Service, TestContext.Current.CancellationToken));
        await fixture.DispatchAsync(new HydrateConversationAction("conv-1",
            ImmutableList<ConversationMessageSnapshot>.Empty, ImmutableList<ConversationPlanEntrySnapshot>.Empty, false));
        peer.Service.PublishBufferedUpdate(captured);
        await DrainVersionedUpdatesAsync(fixture, dispatcher);

        var transcript = (await fixture.ChatStore.GetCurrentStateAsync()).ResolveContentSlice("conv-1")?.Transcript;
        Assert.DoesNotContain(transcript ?? ImmutableList<ConversationMessageSnapshot>.Empty,
            static message => message.TextContent == "old context");
    }

    private static async Task DrainVersionedUpdatesAsync(ViewModelFixture fixture, QueueingSynchronizationContext dispatcher)
    {
        await dispatcher.RunUntilIdleAsync();
        await AwaitPermissionUiSignalAsync(dispatcher, WaitForPendingSessionUpdatesAsync(fixture.ViewModel));
        await dispatcher.RunUntilIdleAsync();
    }

    private sealed class VersionedUpdatePeer : IAcpTransport
    {
        private readonly AcpClient _client;
        internal AcpChatServiceAdapter Service { get; }
        internal List<string> SentMethods { get; } = [];
        internal Action? AfterConfigResponse { get; set; }
        public bool IsConnected { get; private set; } = true;
        public event EventHandler<AcpTransportMessageReceivedEventArgs>? MessageReceived;
        public event EventHandler<AcpTransportErrorEventArgs>? ErrorOccurred { add { } remove { } }

        private VersionedUpdatePeer()
        {
            _client = new AcpClient(this);
            AcpChatServiceAdapter? adapter = null;
            var events = new AcpEventAdapter(update => adapter!.PublishBufferedUpdate(update), new ImmediateUiDispatcher());
            adapter = new AcpChatServiceAdapter(new ChatService(_client, Mock.Of<IErrorLogger>(), Mock.Of<ISessionManager>()), events);
            Service = adapter;
            events.ReleaseUnscopedBufferedUpdates(lowTrust: false);
        }

        internal static async Task<VersionedUpdatePeer> CreateAsync()
        {
            var peer = new VersionedUpdatePeer();
            await peer._client.InitializeDraftAsync(new InitializeParams(new ClientInfo("view-test", "1"), new ClientCapabilities())
            { ProtocolVersion = AcpProtocolVersion.V2 }, TestContext.Current.CancellationToken);
            return peer;
        }

        internal void Update(string update) => Deliver("{\"jsonrpc\":\"2.0\",\"method\":\"session/update\",\"params\":{\"sessionId\":\"remote-1\",\"update\":" + update + "}}");
        internal Task<bool> DisconnectClientAsync() => _client.DisconnectAsync();
        private void Deliver(string message) => MessageReceived?.Invoke(this, new(message));
        public Task<bool> ConnectAsync(CancellationToken cancellationToken = default) { IsConnected = true; return Task.FromResult(true); }
        public Task<bool> DisconnectAsync() { IsConnected = false; return Task.FromResult(true); }
        public Task<bool> SendMessageAsync(string message, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var document = JsonDocument.Parse(message);
            var root = document.RootElement;
            if (!root.TryGetProperty("method", out var method)) return Task.FromResult(true);
            SentMethods.Add(method.GetString()!);
            if (method.ValueEquals("initialize"))
            {
                var result = JsonSerializer.Serialize(new InitializeResponse(AcpProtocolVersion.V2,
                    new AgentInfo("view-peer", "1"), new AgentCapabilities()), AcpWireFormat.For(2).TypeInfo<InitializeResponse>());
                Deliver("{\"jsonrpc\":\"2.0\",\"id\":" + root.GetProperty("id").GetRawText() + ",\"result\":" + result + "}");
            }
            else if (method.ValueEquals("session/set_config_option"))
            {
                Deliver("{\"jsonrpc\":\"2.0\",\"id\":" + root.GetProperty("id").GetRawText()
                    + ",\"result\":{\"configOptions\":[{\"configId\":\"auto\",\"name\":\"Automatic\",\"type\":\"boolean\",\"currentValue\":true}]}}");
                AfterConfigResponse?.Invoke();
            }
            return Task.FromResult(true);
        }
        public void Dispose() => Service.Dispose();
    }
}
