using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text.Json;
using Moq;
using SalmonEgg.Acp.Client;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.ViewModels.Chat;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public partial class ChatViewModelTests
{
    [Fact]
    public async Task PermissionRequest_OldQueuedEventAfterConnectionReplacement_DoesNotDisplayOldPrompt()
    {
        // Arrange
        var dispatcher = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(dispatcher);
        using var oldPeer = await PermissionUiPeer.CreateAsync();
        using var currentPeer = await PermissionUiPeer.CreateAsync();
        await AttachPermissionPeerAsync(fixture, dispatcher, oldPeer);
        dispatcher.Enqueue(() => fixture.ViewModel.ReplaceChatService(currentPeer.Service));
        oldPeer.Request("permission", "remote-1", "old-tool");

        // Act
        await dispatcher.RunUntilIdleAsync();

        // Assert
        Assert.Null(fixture.ViewModel.PendingPermissionRequest);
        Assert.False(fixture.ViewModel.ShowPermissionDialog);
        currentPeer.Request("permission", "remote-1", "current-tool");
        await dispatcher.RunUntilIdleAsync();
        Assert.Contains("current-tool", fixture.ViewModel.PendingPermissionRequest!.ToolCallJson);
        Assert.Empty(currentPeer.Responses);
    }

    [Fact]
    public async Task PermissionRequest_OldCommandAfterConnectionReplacement_CannotAnswerReusedId()
    {
        // Arrange
        var dispatcher = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(dispatcher);
        using var oldPeer = await PermissionUiPeer.CreateAsync();
        using var currentPeer = await PermissionUiPeer.CreateAsync();
        await AttachPermissionPeerAsync(fixture, dispatcher, oldPeer);
        oldPeer.Request("permission", "remote-1", "old-tool");
        await dispatcher.RunUntilIdleAsync();
        var oldPrompt = Assert.IsType<PermissionRequestViewModel>(fixture.ViewModel.PendingPermissionRequest);
        await AwaitWithSynchronizationContextAsync(dispatcher,
            fixture.ViewModel.ReplaceChatServiceAsync(currentPeer.Service, TestContext.Current.CancellationToken));
        currentPeer.Request("permission", "remote-1", "current-tool");
        await dispatcher.RunUntilIdleAsync();
        var currentPrompt = fixture.ViewModel.PendingPermissionRequest;

        // Act
        await AwaitWithSynchronizationContextAsync(dispatcher, oldPrompt.RespondCommand.ExecuteAsync(oldPrompt.Options[0]));

        // Assert
        Assert.Empty(currentPeer.Responses);
        Assert.Same(currentPrompt, fixture.ViewModel.PendingPermissionRequest);
        Assert.True(fixture.ViewModel.ShowPermissionDialog);
        await AwaitWithSynchronizationContextAsync(dispatcher,
            currentPrompt!.RespondCommand.ExecuteAsync(currentPrompt.Options[0]));
        Assert.Equal("allow", Assert.Single(currentPeer.Responses).GetProperty("result").GetProperty("outcome").GetProperty("optionId").GetString());
    }

    [Fact]
    public async Task PermissionRequest_OldResponseCompletesAfterReplacement_DoesNotDismissCurrentPrompt()
    {
        // Arrange
        var dispatcher = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(dispatcher);
        using var oldPeer = await PermissionUiPeer.CreateAsync();
        using var currentPeer = await PermissionUiPeer.CreateAsync();
        await AttachPermissionPeerAsync(fixture, dispatcher, oldPeer);
        oldPeer.Request("permission", "remote-1", "old-tool");
        await dispatcher.RunUntilIdleAsync();
        var oldPrompt = fixture.ViewModel.PendingPermissionRequest!;
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        oldPeer.ResponseSend = (_, token) =>
        {
            started.TrySetResult(true);
            return release.Task.WaitAsync(token);
        };
        var oldResponse = oldPrompt.RespondCommand.ExecuteAsync(oldPrompt.Options[0]);
        try
        {
            await AwaitWithSynchronizationContextAsync(dispatcher, started.Task);
            await AwaitWithSynchronizationContextAsync(dispatcher,
                fixture.ViewModel.ReplaceChatServiceAsync(currentPeer.Service, TestContext.Current.CancellationToken));
            currentPeer.Request("permission", "remote-1", "current-tool");
            await dispatcher.RunUntilIdleAsync();
            var currentPrompt = fixture.ViewModel.PendingPermissionRequest;

            // Act
            release.TrySetResult(true);
            await AwaitWithSynchronizationContextAsync(dispatcher, oldResponse);

            // Assert
            Assert.Same(currentPrompt, fixture.ViewModel.PendingPermissionRequest);
            Assert.True(fixture.ViewModel.ShowPermissionDialog);
            Assert.Empty(currentPeer.Responses);
        }
        finally
        {
            release.TrySetResult(true);
            await AwaitWithSynchronizationContextAsync(dispatcher, oldResponse);
        }
    }

    [Fact]
    public async Task PermissionRequest_SwitchConversation_PreservesOnlyItsOwnPromptAndInlineIdentity()
    {
        // Arrange
        var dispatcher = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(dispatcher);
        using var peer = await PermissionUiPeer.CreateAsync();
        await AttachPermissionPeerAsync(fixture, dispatcher, peer);
        await AddPermissionToolCardAsync(fixture, "conv-1", "shared-tool-id");
        await AddPermissionToolCardAsync(fixture, "conv-2", "shared-tool-id");
        peer.Request("first", "remote-1", "shared-tool-id");
        await dispatcher.RunUntilIdleAsync();
        var first = fixture.ViewModel.PendingPermissionRequest;
        var firstCard = Assert.Single(fixture.ViewModel.MessageHistory);
        Assert.Same(first, firstCard.PendingPermissionRequest);

        // Act / Assert
        await SelectPermissionConversationAsync(fixture, "conv-2");
        Assert.Null(fixture.ViewModel.PendingPermissionRequest);
        Assert.False(fixture.ViewModel.ShowPermissionDialog);
        Assert.Null(Assert.Single(fixture.ViewModel.MessageHistory).PendingPermissionRequest);
        peer.Request("second", "remote-2", "shared-tool-id");
        await dispatcher.RunUntilIdleAsync();
        var second = fixture.ViewModel.PendingPermissionRequest;
        Assert.NotSame(first, second);
        var secondCard = Assert.Single(fixture.ViewModel.MessageHistory);
        Assert.Same(second, secondCard.PendingPermissionRequest);
        var crossConversationProjection = false;
        secondCard.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ChatMessageViewModel.PendingPermissionRequest)
                && ReferenceEquals(secondCard.PendingPermissionRequest, first))
            {
                crossConversationProjection = true;
            }
        };
        await SelectPermissionConversationAsync(fixture, "conv-1");
        Assert.Same(first, fixture.ViewModel.PendingPermissionRequest);
        Assert.Same(first, Assert.Single(fixture.ViewModel.MessageHistory).PendingPermissionRequest);
        Assert.False(crossConversationProjection);
        await AwaitWithSynchronizationContextAsync(dispatcher, first!.RespondCommand.ExecuteAsync(first.Options[0]));
        Assert.Equal("first", Assert.Single(peer.Responses).GetProperty("id").GetString());
        await SelectPermissionConversationAsync(fixture, "conv-2");
        Assert.Same(second, fixture.ViewModel.PendingPermissionRequest);
        Assert.Same(second, Assert.Single(fixture.ViewModel.MessageHistory).PendingPermissionRequest);
        Assert.True(fixture.ViewModel.ShowPermissionDialog);
    }

    [Fact]
    public async Task PermissionRequest_SameClientReconnectWithReusedId_ReplacesInvalidPrompt()
    {
        // Arrange
        var dispatcher = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(dispatcher);
        using var peer = await PermissionUiPeer.CreateAsync();
        await AttachPermissionPeerAsync(fixture, dispatcher, peer);
        peer.Request("permission", "remote-1", "old-tool");
        await dispatcher.RunUntilIdleAsync();
        var old = fixture.ViewModel.PendingPermissionRequest!;

        // Act
        await peer.ReconnectAsync();
        peer.Request("permission", "remote-1", "current-tool");
        await dispatcher.RunUntilIdleAsync();
        var current = fixture.ViewModel.PendingPermissionRequest!;
        await AwaitWithSynchronizationContextAsync(dispatcher, old.RespondCommand.ExecuteAsync(old.Options[0]));

        // Assert
        Assert.NotSame(old, current);
        Assert.Contains("current-tool", current.ToolCallJson);
        Assert.Empty(peer.Responses);
        await AwaitWithSynchronizationContextAsync(dispatcher, current.RespondCommand.ExecuteAsync(current.Options[0]));
        Assert.Single(peer.Responses);
        Assert.False(fixture.ViewModel.ShowPermissionDialog);
    }

    [Fact]
    public async Task PermissionRequest_MultipleRequestsInConversation_KeepsFirstUntilAnswered()
    {
        // Arrange
        var dispatcher = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(dispatcher);
        using var peer = await PermissionUiPeer.CreateAsync();
        await AttachPermissionPeerAsync(fixture, dispatcher, peer);
        peer.Request("first", "remote-1", "first-tool");
        await dispatcher.RunUntilIdleAsync();
        var first = fixture.ViewModel.PendingPermissionRequest!;

        // Act
        peer.Request("second", "remote-1", "second-tool");
        await dispatcher.RunUntilIdleAsync();

        // Assert
        Assert.Same(first, fixture.ViewModel.PendingPermissionRequest);
        await AwaitWithSynchronizationContextAsync(dispatcher, first.RespondCommand.ExecuteAsync(first.Options[0]));
        var second = fixture.ViewModel.PendingPermissionRequest!;
        Assert.NotSame(first, second);
        Assert.Equal("second", second.MessageId.ToString());
        Assert.True(fixture.ViewModel.ShowPermissionDialog);
        await AwaitWithSynchronizationContextAsync(dispatcher, second.RespondCommand.ExecuteAsync(null));
        Assert.Equal(["first", "second"], peer.Responses.Select(response => response.GetProperty("id").GetString()));
        Assert.Null(fixture.ViewModel.PendingPermissionRequest);
    }

    [Fact]
    public async Task PermissionRequest_DecoratorForwardsInnerSender_UsesSubscribedService()
    {
        // Arrange
        var dispatcher = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(dispatcher);
        using var peer = await PermissionUiPeer.CreateAsync();
        using var adapter = new AcpChatServiceAdapter(peer.Service, new AcpEventAdapter(_ => { }, dispatcher));
        await AttachPermissionPeerAsync(fixture, dispatcher, peer, adapter);

        // Act
        peer.Request("permission", "remote-1", "tool");
        await dispatcher.RunUntilIdleAsync();

        // Assert
        var prompt = Assert.IsType<PermissionRequestViewModel>(fixture.ViewModel.PendingPermissionRequest);
        await AwaitWithSynchronizationContextAsync(dispatcher, prompt.RespondCommand.ExecuteAsync(prompt.Options[0]));
        Assert.Single(peer.Responses);
        Assert.False(fixture.ViewModel.ShowPermissionDialog);
    }

    [Fact]
    public async Task PermissionRequest_PoolOnlyReplacement_KeepsLiveOriginalConversationOwner()
    {
        // Arrange
        var dispatcher = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(dispatcher);
        using var originalPeer = await PermissionUiPeer.CreateAsync();
        using var currentPeer = await PermissionUiPeer.CreateAsync();
        await AttachPermissionPeerAsync(fixture, dispatcher, originalPeer);
        originalPeer.Request("permission", "remote-1", "tool-one");
        await dispatcher.RunUntilIdleAsync();
        var original = fixture.ViewModel.PendingPermissionRequest!;

        // Act
        await AwaitWithSynchronizationContextAsync(dispatcher,
            fixture.ViewModel.ReplaceChatServiceWithIntentAsync(currentPeer.Service, ServiceReplaceIntent.PoolOnly,
                TestContext.Current.CancellationToken));
        await SelectPermissionConversationAsync(fixture, "conv-2");
        currentPeer.Request("permission", "remote-2", "tool-two");
        await dispatcher.RunUntilIdleAsync();
        var current = fixture.ViewModel.PendingPermissionRequest!;
        await AwaitWithSynchronizationContextAsync(dispatcher, original.RespondCommand.ExecuteAsync(original.Options[0]));

        // Assert
        Assert.Single(originalPeer.Responses);
        Assert.Empty(currentPeer.Responses);
        Assert.Same(current, fixture.ViewModel.PendingPermissionRequest);
        Assert.True(fixture.ViewModel.ShowPermissionDialog);
    }

    [Fact]
    public async Task PermissionRequest_FailedSessionCancellation_StaysVisibleAndCanRetryCancellation()
    {
        // Arrange
        var dispatcher = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(dispatcher);
        using var peer = await PermissionUiPeer.CreateAsync();
        await AttachPermissionPeerAsync(fixture, dispatcher, peer);
        peer.Request("permission", "remote-1", "tool");
        await dispatcher.RunUntilIdleAsync();
        var prompt = fixture.ViewModel.PendingPermissionRequest!;
        peer.ResponseSend = (_, _) => Task.FromResult(false);

        // Act
        await AwaitWithSynchronizationContextAsync(dispatcher, fixture.ViewModel.CancelSessionCommand.ExecuteAsync(null));

        // Assert
        Assert.Same(prompt, fixture.ViewModel.PendingPermissionRequest);
        Assert.True(fixture.ViewModel.ShowPermissionDialog);
        Assert.Empty(peer.Responses);
        peer.ResponseSend = null;
        await AwaitWithSynchronizationContextAsync(dispatcher, prompt.RespondCommand.ExecuteAsync(null));
        Assert.Equal("cancelled", Assert.Single(peer.Responses).GetProperty("result").GetProperty("outcome").GetProperty("outcome").GetString());
        Assert.False(fixture.ViewModel.ShowPermissionDialog);
    }

    [Fact]
    public async Task PermissionRequest_DisconnectedClient_ProjectionRemovesStalePrompt()
    {
        // Arrange
        var dispatcher = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(dispatcher);
        using var peer = await PermissionUiPeer.CreateAsync();
        await AttachPermissionPeerAsync(fixture, dispatcher, peer);
        peer.Request("permission", "remote-1", "tool");
        await dispatcher.RunUntilIdleAsync();
        Assert.True(fixture.ViewModel.ShowPermissionDialog);

        // Act
        await peer.DisconnectClientAsync();
        await fixture.ApplyCurrentStoreProjectionAsync();

        // Assert
        Assert.Null(fixture.ViewModel.PendingPermissionRequest);
        Assert.False(fixture.ViewModel.ShowPermissionDialog);
        Assert.Empty(peer.Responses);
    }

    [Fact]
    public async Task PermissionRequest_BindingChangesBeforeUserResponse_DoesNotSendChoice()
    {
        // Arrange
        var dispatcher = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(dispatcher);
        using var peer = await PermissionUiPeer.CreateAsync();
        await AttachPermissionPeerAsync(fixture, dispatcher, peer);
        peer.Request("permission", "remote-1", "tool");
        await dispatcher.RunUntilIdleAsync();
        var prompt = fixture.ViewModel.PendingPermissionRequest!;
        await fixture.UpdateStateAsync(state => state with
        {
            Bindings = state.Bindings!.SetItem("conv-1", new("conv-1", "different-remote", "profile"))
        });

        // Act
        await AwaitWithSynchronizationContextAsync(dispatcher, prompt.RespondCommand.ExecuteAsync(prompt.Options[0]));

        // Assert
        Assert.Empty(peer.Responses);
    }

    [Fact]
    public async Task PermissionRequest_UnknownSession_CancelsWithoutDisplaying()
    {
        // Arrange
        var dispatcher = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(dispatcher);
        using var peer = await PermissionUiPeer.CreateAsync();
        await AttachPermissionPeerAsync(fixture, dispatcher, peer);

        // Act
        peer.Request("permission", "unknown", "tool");
        await dispatcher.RunUntilIdleAsync();

        // Assert
        Assert.Equal("cancelled", Assert.Single(peer.Responses).GetProperty("result").GetProperty("outcome").GetProperty("outcome").GetString());
        Assert.Null(fixture.ViewModel.PendingPermissionRequest);
        Assert.False(fixture.ViewModel.ShowPermissionDialog);
    }

    [Fact]
    public async Task PermissionRequest_ResponseFails_StaysVisibleAndRetriesOriginalRequest()
    {
        // Arrange
        var dispatcher = new QueueingSynchronizationContext();
        await using var fixture = CreateViewModel(dispatcher);
        using var peer = await PermissionUiPeer.CreateAsync();
        await AttachPermissionPeerAsync(fixture, dispatcher, peer);
        peer.Request("permission", "remote-1", "tool");
        await dispatcher.RunUntilIdleAsync();
        var prompt = fixture.ViewModel.PendingPermissionRequest!;
        peer.ResponseSend = (_, _) => Task.FromResult(false);

        // Act
        await AwaitWithSynchronizationContextAsync(dispatcher, prompt.RespondCommand.ExecuteAsync(prompt.Options[0]));

        // Assert
        Assert.Same(prompt, fixture.ViewModel.PendingPermissionRequest);
        Assert.True(fixture.ViewModel.ShowPermissionDialog);
        Assert.Empty(peer.Responses);
        peer.ResponseSend = null;
        await AwaitWithSynchronizationContextAsync(dispatcher, prompt.RespondCommand.ExecuteAsync(prompt.Options[0]));
        Assert.Single(peer.Responses);
        Assert.Null(fixture.ViewModel.PendingPermissionRequest);
        Assert.False(fixture.ViewModel.ShowPermissionDialog);
    }

    private static async Task AttachPermissionPeerAsync(
        ViewModelFixture fixture, QueueingSynchronizationContext dispatcher, PermissionUiPeer peer, IChatService? service = null)
    {
        await AwaitWithSynchronizationContextAsync(dispatcher,
            fixture.ViewModel.ReplaceChatServiceAsync(service ?? peer.Service, TestContext.Current.CancellationToken));
        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-1", new("conv-1", "remote-1", "profile"))
                .Add("conv-2", new("conv-2", "remote-2", "profile"))
        });
        await dispatcher.RunUntilIdleAsync();
    }

    private static async Task SelectPermissionConversationAsync(ViewModelFixture fixture, string conversationId)
    {
        await fixture.UpdateStateAsync(state => state with { HydratedConversationId = conversationId });
        Assert.Equal(conversationId, fixture.ViewModel.CurrentSessionId);
    }

    private static async Task AddPermissionToolCardAsync(ViewModelFixture fixture, string conversationId, string toolCallId)
        => await fixture.DispatchAsync(new UpsertTranscriptMessageAction(conversationId, new ConversationMessageSnapshot
        {
            Id = conversationId + "-tool",
            ContentType = "tool_call",
            ToolCallId = toolCallId,
            ToolCallStatus = "pending",
            Title = "Run tests"
        }));

    private sealed class PermissionUiPeer : IAcpTransport
    {
        private readonly AcpClient _client;
        private bool _disposed;

        private PermissionUiPeer()
        {
            _client = new AcpClient(this, Mock.Of<IAcpClientLogger>());
            Service = new ChatService(_client, Mock.Of<IErrorLogger>(), Mock.Of<ISessionManager>());
        }

        public ChatService Service { get; }
        public ConcurrentQueue<JsonElement> Responses { get; } = new();
        public Func<JsonElement, CancellationToken, Task<bool>>? ResponseSend { get; set; }
        public bool IsConnected { get; private set; }
        public event EventHandler<AcpTransportMessageReceivedEventArgs>? MessageReceived;
        public event EventHandler<AcpTransportErrorEventArgs>? ErrorOccurred { add { } remove { } }

        public static async Task<PermissionUiPeer> CreateAsync()
        {
            var peer = new PermissionUiPeer();
            await peer.InitializeAsync();
            return peer;
        }

        public async Task ReconnectAsync()
        {
            await DisconnectClientAsync();
            await InitializeAsync();
        }

        public Task<bool> DisconnectClientAsync() => _client.DisconnectAsync();

        public void Request(string id, string sessionId, string toolCallId)
            => Receive($$$"""{"jsonrpc":"2.0","id":"{{{id}}}","method":"session/request_permission","params":{"sessionId":"{{{sessionId}}}","toolCall":{"toolCallId":"{{{toolCallId}}}","title":"Run tests"},"options":[{"optionId":"allow","name":"Allow once","kind":"allow_once"}]}}""");

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
            using var document = JsonDocument.Parse(message);
            var root = document.RootElement;
            if (root.TryGetProperty("method", out var method))
            {
                if (method.GetString() == "initialize")
                {
                    Receive($$$$"""{"jsonrpc":"2.0","id":{{{{root.GetProperty("id").GetRawText()}}}},"result":{"protocolVersion":1,"agentInfo":{"name":"Peer","version":"1"},"agentCapabilities":{}}}""");
                }
                return true;
            }

            var response = root.Clone();
            var sent = ResponseSend is null || await ResponseSend(response, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (sent)
            {
                Responses.Enqueue(response);
            }
            return sent;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            IsConnected = false;
            Service.Dispose();
        }

        private void Receive(string message) => MessageReceived?.Invoke(this, new(message));

        private Task<InitializeResponse> InitializeAsync()
            => _client.InitializeAsync(new InitializeParams(new ClientInfo("Permission UI test", "1"),
                ClientCapabilityDefaults.Create()), TestContext.Current.CancellationToken);
    }
}
