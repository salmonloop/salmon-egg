using System.Collections.Immutable;
using System.Text.Json;
using Moq;
using SalmonEgg.Acp.Client;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Domain.Interfaces;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Services.Chat;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public partial class ChatViewModelTests
{
    [Fact]
    public async Task SessionConfiguration_ResponseThenUpdate_BlocksDelayedApplyFromRestoringOlderValues()
    {
        // Arrange: actual SDK -> ChatService -> buffered event adapter -> ChatViewModel.
        var dispatcher = new QueueingSynchronizationContext();
        await using var fixture = CreateInteractionViewModel(dispatcher);
        using var peer = await ConfigurationUiPeer.CreateAsync(dispatcher);
        RegisterInteractionService(fixture, peer.Service);
        await dispatcher.RunUntilCompletedAsync(fixture.ViewModel.ReplaceChatServiceAsync(peer.Service,
            TestContext.Current.CancellationToken));
        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conversation",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conversation", new("conversation", "remote", "profile")),
            ConversationSessionStates = ImmutableDictionary<string, ConversationSessionStateSlice>.Empty
                .Add("conversation", new([], null, [MakeConfigSnapshot(false)], true, [], null, null))
        });
        var row = Assert.Single(fixture.ViewModel.ConfigOptions);
        row.SelectedOption = row.Options[1];

        // Act: keep the set call suspended after A is received; B reaches its real projection first.
        var apply = row.ApplyCommand.ExecuteAsync(null);
        while (!peer.RequestSent.Task.IsCompleted)
        {
            dispatcher.RunAll();
            await Task.Yield();
            TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        }
        await dispatcher.RunUntilIdleAsync();
        peer.ReleaseSend.TrySetResult(true);
        await dispatcher.RunUntilCompletedAsync(apply);
        await dispatcher.RunUntilIdleAsync();

        // Assert
        var current = Assert.Single((await fixture.GetStateAsync()).ResolveSessionStateSlice("conversation")!.Value.ConfigOptions);
        Assert.Equal("agent-choice", current.SelectedValue);
        Assert.Equal("agent-choice", Assert.Single(fixture.ViewModel.ConfigOptions).Value);
    }

    private sealed class ConfigurationUiPeer : IAcpTransport
    {
        private readonly AcpClient _client;
        private readonly AcpEventAdapter _adapter;

        private ConfigurationUiPeer(IUiDispatcher dispatcher)
        {
            _client = new AcpClient(this, new NullAcpClientLogger());
            _adapter = new AcpEventAdapter(update => Service!.PublishBufferedUpdate(update), dispatcher);
            Service = new AcpChatServiceAdapter(new ChatService(_client, Mock.Of<IErrorLogger>(),
                CreateSessionManagerWithStore().Object), _adapter);
            _adapter.ReleaseUnscopedBufferedUpdates();
        }

        public AcpChatServiceAdapter Service { get; }
        public TaskCompletionSource RequestSent { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> ReleaseSend { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool IsConnected { get; private set; }
        public event EventHandler<AcpTransportMessageReceivedEventArgs>? MessageReceived;
        public event EventHandler<AcpTransportErrorEventArgs>? ErrorOccurred { add { } remove { } }

        public static async Task<ConfigurationUiPeer> CreateAsync(IUiDispatcher dispatcher)
        {
            var peer = new ConfigurationUiPeer(dispatcher);
            await peer._client.InitializeAsync(new InitializeParams(new ClientInfo("config-tests", "1"),
                ClientCapabilityDefaults.Create()), TestContext.Current.CancellationToken);
            return peer;
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
            using var document = JsonDocument.Parse(message);
            var request = document.RootElement;
            var id = request.GetProperty("id").GetRawText();
            if (request.GetProperty("method").GetString() == "initialize")
            {
                Receive($$$$"""{"jsonrpc":"2.0","id":{{{{id}}}},"result":{"protocolVersion":1,"agentCapabilities":{}}}""");
                return true;
            }
            Receive($$$$"""{"jsonrpc":"2.0","id":{{{{id}}}},"result":{"configOptions":[{{{{ConfigJson("careful")}}}}]}}""");
            Receive($$$$"""{"jsonrpc":"2.0","method":"session/update","params":{"sessionId":"remote","update":{"sessionUpdate":"config_option_update","configOptions":[{{{{ConfigJson("agent-choice")}}}}]}}}""");
            RequestSent.TrySetResult();
            return await ReleaseSend.Task.WaitAsync(cancellationToken);
        }

        public void Dispose()
        {
            ReleaseSend.TrySetResult(false);
            IsConnected = false;
            Service.Dispose();
        }

        private static string ConfigJson(string value)
            => $$"""{"id":"profile-setting","name":"Performance","type":"select","currentValue":"{{value}}","options":[{"value":"fast","name":"Fast"},{"value":"careful","name":"Careful"},{"value":"agent-choice","name":"Agent choice"}]}""";

        private void Receive(string message) => MessageReceived?.Invoke(this, new(message));
    }
}
