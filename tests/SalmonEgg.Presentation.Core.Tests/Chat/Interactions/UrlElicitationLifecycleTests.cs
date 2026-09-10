using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SalmonEgg.Acp.Client;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.ViewModels.Chat.Elicitation;
using SalmonEgg.Presentation.ViewModels.Chat.Interactions;
using SalmonEgg.Presentation.ViewModels.Chat.Panels;

namespace SalmonEgg.Presentation.Core.Tests.Chat.Interactions;

public sealed class UrlElicitationLifecycleTests
{
    [Fact]
    public async Task SessionCancellation_RetiresUrlPromptAndAllowsNextRequest()
    {
        // Arrange
        using var fixture = new LifecycleFixture();
        await fixture.InitializeAsync();
        using var first = await fixture.DeliverAsync(901);
        var firstEvent = Assert.Single(fixture.Requests);
        Assert.True(first.CanSubmit);

        // Act: use the same Application operation as the chat stop command.
        await fixture.Service.CancelSessionAsync(new SessionCancelParams("remote"));

        // Assert
        Assert.Equal("cancel", firstEvent.State.ResponseAction);
        Assert.Equal("cancel", Assert.Single(fixture.Responses).GetProperty("result").GetProperty("action").GetString());
        Assert.Null(fixture.Panels.GetPendingElicitationRequest("conversation"));
        using var next = await fixture.DeliverAsync(902);
        Assert.Same(next, fixture.Panels.GetPendingElicitationRequest("conversation"));
        Assert.True(next.CanSubmit);
        Assert.Empty(first.FullUrl);
        Assert.Equal(1, fixture.Dismissals);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TerminalResponse_SentOutsideCardCommand_RetiresOnUiDispatcher(bool decline)
    {
        // Arrange
        using var fixture = new LifecycleFixture();
        await fixture.InitializeAsync();
        var dispatcher = new QueuedDispatcher();
        fixture.DispatchAsync = dispatcher.Enqueue;
        fixture.BeforeDismiss = () => Assert.True(dispatcher.IsExecuting);
        using var card = await fixture.DeliverAsync(11);
        var request = Assert.Single(fixture.Requests);

        // Act
        Assert.True(await (decline ? request.Decline() : request.Cancel()));

        // Assert: SDK completion may happen off-thread; only the UI projection may clear the slot.
        Assert.Same(card, fixture.Panels.GetPendingElicitationRequest("conversation"));
        Assert.Equal(0, fixture.Dismissals);
        dispatcher.Drain();
        Assert.Null(fixture.Panels.GetPendingElicitationRequest("conversation"));
        Assert.Equal(1, fixture.Dismissals);
        Assert.Equal(decline ? "decline" : "cancel", request.State.ResponseAction);
        Assert.Single(fixture.Responses);
        Assert.False(await request.Cancel());
        dispatcher.Drain();
        Assert.Equal(1, fixture.Dismissals);
    }

    [Fact]
    public async Task CancelSession_FailedResponse_RetainsCardUntilSuccessfulRetry()
    {
        // Arrange
        using var fixture = new LifecycleFixture();
        await fixture.InitializeAsync();
        using var card = await fixture.DeliverAsync(12);
        var request = Assert.Single(fixture.Requests);
        fixture.WriteResponseAsync = _ => Task.FromResult(false);

        // Act
        await fixture.Service.CancelSessionAsync(new SessionCancelParams("remote"));

        // Assert
        Assert.Null(request.State.ResponseAction);
        Assert.Same(card, fixture.Panels.GetPendingElicitationRequest("conversation"));
        Assert.False(card.CanSubmit);
        Assert.True(card.CanCancel);
        Assert.Equal(0, fixture.Dismissals);
        fixture.WriteResponseAsync = _ => Task.FromResult(true);
        await card.CancelCommand.ExecuteAsync(null);
        Assert.Equal("cancel", request.State.ResponseAction);
        Assert.Null(fixture.Panels.GetPendingElicitationRequest("conversation"));
        Assert.Equal(1, fixture.Dismissals);
        Assert.Equal("cancel", Assert.Single(fixture.Responses).GetProperty("result").GetProperty("action").GetString());
    }

    [Fact]
    public async Task CancelSession_ResponseStillWriting_DoesNotRetireBeforeSendCompletes()
    {
        // Arrange
        using var fixture = new LifecycleFixture();
        await fixture.InitializeAsync();
        using var card = await fixture.DeliverAsync(13);
        var request = Assert.Single(fixture.Requests);
        var writeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writeCompleted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.WriteResponseAsync = _ =>
        {
            writeStarted.TrySetResult();
            return writeCompleted.Task;
        };

        // Act
        var cancelling = fixture.Service.CancelSessionAsync(new SessionCancelParams("remote"));
        await writeStarted.Task.WaitAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(request.State.ResponseAction);
        Assert.Same(card, fixture.Panels.GetPendingElicitationRequest("conversation"));
        Assert.Equal(0, fixture.Dismissals);
        writeCompleted.SetResult(true);
        await cancelling.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Null(fixture.Panels.GetPendingElicitationRequest("conversation"));
        Assert.Equal(1, fixture.Dismissals);
    }

    [Fact]
    public async Task Accept_ResponseAndCompletion_KeepNoticeUntilDismissed()
    {
        // Arrange
        using var fixture = new LifecycleFixture();
        await fixture.InitializeAsync();
        using var card = await fixture.DeliverAsync(14);

        // Act
        await card.SubmitCommand.ExecuteAsync(null);

        // Assert
        Assert.True(card.IsAwaitingCompletion);
        Assert.False(card.IsCompleted);
        Assert.Same(card, fixture.Panels.GetPendingElicitationRequest("conversation"));
        Assert.Equal(0, fixture.Dismissals);
        fixture.CompleteUrl(14);
        Assert.True(card.IsCompleted);
        Assert.Same(card, fixture.Panels.GetPendingElicitationRequest("conversation"));
        await card.DismissCommand.ExecuteAsync(null);
        Assert.Null(fixture.Panels.GetPendingElicitationRequest("conversation"));
        Assert.Equal(1, fixture.Dismissals);
        Assert.Equal("accept", Assert.Single(fixture.Responses).GetProperty("result").GetProperty("action").GetString());
    }

    [Fact]
    public async Task CancelledNotification_ReconnectReusesIds_DoesNotClearReplacement()
    {
        // Arrange
        using var fixture = new LifecycleFixture();
        await fixture.InitializeAsync();
        var dispatcher = new QueuedDispatcher();
        fixture.DispatchAsync = dispatcher.Enqueue;
        using var original = await fixture.DeliverAsync(15);

        // Act: defer the old state notification until a new connection owns the same wire ids.
        await fixture.Service.CancelSessionAsync(new SessionCancelParams("remote"));
        Assert.Same(original, fixture.Panels.GetPendingElicitationRequest("conversation"));
        fixture.Panels.RemoveConversation("conversation", isCurrentConversation: false);
        await fixture.Client.DisconnectAsync();
        await fixture.InitializeAsync();
        using var replacement = await fixture.DeliverAsync(15);
        dispatcher.Drain();

        // Assert
        Assert.Same(replacement, fixture.Panels.GetPendingElicitationRequest("conversation"));
        Assert.True(replacement.CanSubmit);
        Assert.Empty(original.FullUrl);
        Assert.Equal(0, fixture.Dismissals);
        Assert.False(await fixture.Requests[0].Cancel());
        Assert.Null(fixture.Requests[1].State.ResponseAction);
    }

    private sealed class LifecycleFixture : IDisposable
    {
        private readonly Mock<IAcpTransport> _transport = new();
        private readonly ChatInteractionEventBridge _bridge;

        public LifecycleFixture()
        {
            _transport.SetupGet(value => value.IsConnected).Returns(true);
            _transport.Setup(value => value.DisconnectAsync()).ReturnsAsync(true);
            _transport.Setup(value => value.SendMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns<string, CancellationToken>(SendAsync);
            Client = new AcpClient(_transport.Object, Mock.Of<IAcpClientLogger>());
            Service = new ChatService(Client, Mock.Of<IErrorLogger>(), Mock.Of<ISessionManager>());
            Service.ElicitationRequestReceived += OnRequest;
            var router = new Mock<IAuthoritativeRemoteSessionRouter>();
            router.Setup(value => value.ResolveConversationIdAsync("remote", It.IsAny<CancellationToken>()))
                .ReturnsAsync("conversation");
            var launcher = new Mock<IExternalUriLauncher>();
            launcher.SetupGet(value => value.IsSupported).Returns(true);
            launcher.Setup(value => value.OpenAsync(It.IsAny<ExternalUriTarget>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ExternalUriOpenResult.Opened);
            _bridge = new ChatInteractionEventBridge(router.Object, new ChatTerminalProjectionCoordinator(), uriLauncher: launcher.Object);
        }

        public AcpClient Client { get; }
        public ChatService Service { get; }
        public ChatConversationPanelStateCoordinator Panels { get; } = new();
        public List<ElicitationRequestEventArgs> Requests { get; } = [];
        public List<JsonElement> Responses { get; } = [];
        public int Dismissals { get; private set; }
        public Action? BeforeDismiss { get; set; }
        public Func<JsonElement, Task<bool>> WriteResponseAsync { get; set; } = _ => Task.FromResult(true);
        public Func<Action, Task> DispatchAsync { get; set; } = action => { action(); return Task.CompletedTask; };

        public Task InitializeAsync() => Client.InitializeAsync(new InitializeParams(new ClientInfo("Test", "1"),
            new ClientCapabilities { Elicitation = new() { Url = new() } }), TestContext.Current.CancellationToken);

        public async Task<ElicitationRequestViewModel> DeliverAsync(int id)
        {
            _transport.Raise(value => value.MessageReceived += null, new AcpTransportMessageReceivedEventArgs(
                $$$"""{"jsonrpc":"2.0","id":{{{id}}},"method":"elicitation/create","params":{"sessionId":"remote","mode":"url","elicitationId":"url-{{{id}}}","url":"https://example.com/authorize","message":"Authorize"}}"""));
            var projection = await _bridge.BuildElicitationRequestAsync(Requests[^1],
                (conversationId, card) =>
                {
                    BeforeDismiss?.Invoke();
                    Dismissals++;
                    Panels.RemoveElicitationRequest(conversationId, card);
                    return Task.CompletedTask;
                }, NullLogger.Instance, DispatchAsync);
            Assert.NotNull(projection);
            Assert.True(Panels.TryStoreElicitationRequest(projection.Value.ConversationId, projection.Value.ViewModel));
            return projection.Value.ViewModel;
        }

        public void CompleteUrl(int id) => _transport.Raise(value => value.MessageReceived += null,
            new AcpTransportMessageReceivedEventArgs(
                $$$"""{"jsonrpc":"2.0","method":"elicitation/complete","params":{"elicitationId":"url-{{{id}}}"}}"""));

        public void Dispose()
        {
            Service.ElicitationRequestReceived -= OnRequest;
            Panels.ClearElicitationRequests();
            Service.Dispose();
            Client.Dispose();
        }

        private void OnRequest(object? sender, ElicitationRequestEventArgs request) => Requests.Add(request);

        private async Task<bool> SendAsync(string message, CancellationToken cancellationToken)
        {
            using var document = JsonDocument.Parse(message);
            var root = document.RootElement;
            if (root.TryGetProperty("method", out var method))
            {
                if (method.GetString() == "initialize")
                {
                    var id = root.GetProperty("id").GetRawText();
                    _transport.Raise(value => value.MessageReceived += null, new AcpTransportMessageReceivedEventArgs(
                        $$$$"""{"jsonrpc":"2.0","id":{{{{id}}}},"result":{"protocolVersion":1,"agentInfo":{"name":"Test","version":"1"},"agentCapabilities":{}}}"""));
                }
                return true;
            }

            var response = root.Clone();
            if (!await WriteResponseAsync(response).WaitAsync(cancellationToken)) return false;
            Responses.Add(response);
            return true;
        }
    }

    private sealed class QueuedDispatcher
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<(Action Action, TaskCompletionSource Completion)> _queue = new();

        public bool IsExecuting { get; private set; }

        public Task Enqueue(Action action)
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _queue.Enqueue((action, completion));
            return completion.Task;
        }

        public void Drain()
        {
            while (_queue.TryDequeue(out var item))
            {
                IsExecuting = true;
                try
                {
                    item.Action();
                    item.Completion.TrySetResult();
                }
                catch (Exception exception)
                {
                    item.Completion.TrySetException(exception);
                    throw;
                }
                finally
                {
                    IsExecuting = false;
                }
            }
        }
    }
}
