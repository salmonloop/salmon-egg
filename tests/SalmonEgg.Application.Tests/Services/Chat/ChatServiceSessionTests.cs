using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Serilog;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Domain.Interfaces;
using SalmonEgg.Domain.Interfaces.Transport;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.Content;
using SalmonEgg.Domain.Models.JsonRpc;
using SalmonEgg.Domain.Models.Protocol;
using SalmonEgg.Domain.Models.Session;
using SalmonEgg.Domain.Services;
using SalmonEgg.Domain.Services.Security;
using SalmonEgg.Infrastructure.Serialization;
using SalmonEgg.Infrastructure.Services;

namespace SalmonEgg.Application.Tests.Services.Chat;

public sealed class ChatServiceSessionTests
{
    [Fact]
    public async Task SendPromptAsync_ForwardsCancellationToken()
    {
        var acpClient = new Mock<IAcpClient>(MockBehavior.Loose);
        var errorLogger = new Mock<IErrorLogger>(MockBehavior.Loose);
        var sessionManager = new SessionManager();

        CancellationToken captured = default;
        acpClient
            .Setup(c => c.SendPromptAsync(It.IsAny<SessionPromptParams>(), It.IsAny<CancellationToken>()))
            .Callback<SessionPromptParams, CancellationToken>((_, ct) => captured = ct)
            .ReturnsAsync(new SessionPromptResponse(StopReason.EndTurn));

        var sut = new ChatService(acpClient.Object, errorLogger.Object, sessionManager);

        using var cts = new CancellationTokenSource();
        await sut.SendPromptAsync(new SessionPromptParams("s1", prompt: new List<ContentBlock>()), cts.Token);

        Assert.Equal(cts.Token, captured);

        sut.Dispose();
    }

    [Fact]
    public void SessionUpdate_IsStoredPerSessionId()
    {
        var acpClient = new Mock<IAcpClient>(MockBehavior.Loose);
        var errorLogger = new Mock<IErrorLogger>(MockBehavior.Loose);
        var sessionManager = new SessionManager();

        var sut = new ChatService(acpClient.Object, errorLogger.Object, sessionManager);

        var update = new AgentMessageUpdate(new TextContentBlock("hello"));
        acpClient.Raise(
            c => c.SessionUpdateReceived += null,
            new SessionUpdateEventArgs("s1", update));

        var session = sessionManager.GetSession("s1");
        Assert.NotNull(session);
        Assert.Single(session!.History);
        Assert.IsType<TextContentBlock>(session.History[0].Content);
        Assert.Equal("hello", ((TextContentBlock)session.History[0].Content!).Text);

        sut.Dispose();
    }

    [Fact]
    public async Task LoadSessionAsync_WhenClientThrows_RestoresCachedHistoryAndPreviousSession()
    {
        var acpClient = new Mock<IAcpClient>(MockBehavior.Loose);
        var errorLogger = new Mock<IErrorLogger>(MockBehavior.Loose);
        var sessionManager = new SessionManager();

        // Seed current session via CreateSessionAsync.
        acpClient.SetupGet(c => c.IsInitialized).Returns(true);
        acpClient.SetupGet(c => c.IsConnected).Returns(true);
        acpClient.SetupGet(c => c.AgentInfo).Returns((AgentInfo?)null);
        acpClient.SetupGet(c => c.AgentCapabilities).Returns((AgentCapabilities?)null);
        acpClient
            .Setup(c => c.CreateSessionAsync(It.IsAny<SessionNewParams>(), default))
            .ReturnsAsync(new SessionNewResponse { SessionId = "s1" });

        // Loading a different session fails.
        acpClient
            .Setup(c => c.LoadSessionAsync(It.IsAny<SessionLoadParams>(), default))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var sut = new ChatService(acpClient.Object, errorLogger.Object, sessionManager);

        await sut.CreateSessionAsync(new SessionNewParams { Cwd = Environment.CurrentDirectory });
        Assert.Equal("s1", sut.CurrentSessionId);

        // Seed cached history for the target session.
        await sessionManager.CreateSessionAsync("s2", cwd: Environment.CurrentDirectory);
        sessionManager.UpdateSession("s2", s => s.AddHistoryEntry(SalmonEgg.Domain.Models.Session.SessionUpdateEntry.CreateMessage(new TextContentBlock("cached"))));

        var before = sessionManager.GetSession("s2")!.History.Count;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.LoadSessionAsync(new SessionLoadParams("s2", Environment.CurrentDirectory)));

        Assert.Equal("s1", sut.CurrentSessionId);
        Assert.Equal(before, sessionManager.GetSession("s2")!.History.Count);

        sut.Dispose();
    }

    [Fact]
    public async Task LoadSessionAsync_WhenTargetSessionIsNotTracked_PreRegistersSessionBeforeClientCall()
    {
        var acpClient = new Mock<IAcpClient>(MockBehavior.Strict);
        var errorLogger = new Mock<IErrorLogger>(MockBehavior.Loose);
        var sessionManager = new SessionManager();

        acpClient.SetupGet(c => c.IsInitialized).Returns(true);
        acpClient.SetupGet(c => c.IsConnected).Returns(true);
        acpClient.SetupGet(c => c.AgentInfo).Returns((AgentInfo?)null);
        acpClient.SetupGet(c => c.AgentCapabilities).Returns((AgentCapabilities?)null);
        acpClient
            .Setup(c => c.LoadSessionAsync(
                It.Is<SessionLoadParams>(p => p.SessionId == "remote-1"),
                default))
            .Callback(() =>
            {
                var tracked = sessionManager.GetSession("remote-1");
                Assert.NotNull(tracked);
                Assert.Equal(Environment.CurrentDirectory, tracked!.Cwd);
            })
            .ReturnsAsync(new SessionLoadResponse());

        var sut = new ChatService(acpClient.Object, errorLogger.Object, sessionManager);

        await sut.LoadSessionAsync(new SessionLoadParams("remote-1", Environment.CurrentDirectory));

        var session = sessionManager.GetSession("remote-1");
        Assert.NotNull(session);
        Assert.Equal(Environment.CurrentDirectory, session!.Cwd);
        Assert.Equal(SessionState.Active, session.State);

        sut.Dispose();
    }

    [Fact]
    public async Task ResumeSessionAsync_WhenTargetSessionIsNotTracked_PreRegistersSessionWithoutClearingCachedHistory()
    {
        var acpClient = new Mock<IAcpClient>(MockBehavior.Strict);
        var errorLogger = new Mock<IErrorLogger>(MockBehavior.Loose);
        var sessionManager = new SessionManager();

        acpClient.SetupGet(c => c.IsInitialized).Returns(true);
        acpClient.SetupGet(c => c.IsConnected).Returns(true);
        acpClient.SetupGet(c => c.AgentInfo).Returns((AgentInfo?)null);
        acpClient.SetupGet(c => c.AgentCapabilities).Returns((AgentCapabilities?)null);
        acpClient
            .Setup(c => c.ResumeSessionAsync(
                It.Is<SessionResumeParams>(p => p.SessionId == "remote-1"),
                default))
            .Callback(() =>
            {
                var tracked = sessionManager.GetSession("remote-1");
                Assert.NotNull(tracked);
                Assert.Equal(Environment.CurrentDirectory, tracked!.Cwd);
            })
            .ReturnsAsync(new SessionResumeResponse());

        var sut = new ChatService(acpClient.Object, errorLogger.Object, sessionManager);

        await sut.ResumeSessionAsync(new SessionResumeParams("remote-1", Environment.CurrentDirectory));

        var session = sessionManager.GetSession("remote-1");
        Assert.NotNull(session);
        Assert.Equal(Environment.CurrentDirectory, session!.Cwd);
        Assert.Equal(SessionState.Active, session.State);
        Assert.Empty(session.History);

        sut.Dispose();
    }

    [Fact]
    public async Task ResumeSessionAsync_WhenTargetSessionHasCachedHistory_PreservesHistory()
    {
        var acpClient = new Mock<IAcpClient>(MockBehavior.Strict);
        var errorLogger = new Mock<IErrorLogger>(MockBehavior.Loose);
        var sessionManager = new SessionManager();

        await sessionManager.CreateSessionAsync("remote-1", cwd: Environment.CurrentDirectory);
        sessionManager.UpdateSession(
            "remote-1",
            s => s.AddHistoryEntry(SalmonEgg.Domain.Models.Session.SessionUpdateEntry.CreateMessage(new TextContentBlock("cached"))));

        acpClient.SetupGet(c => c.IsInitialized).Returns(true);
        acpClient.SetupGet(c => c.IsConnected).Returns(true);
        acpClient.SetupGet(c => c.AgentInfo).Returns((AgentInfo?)null);
        acpClient.SetupGet(c => c.AgentCapabilities).Returns((AgentCapabilities?)null);
        acpClient
            .Setup(c => c.ResumeSessionAsync(It.IsAny<SessionResumeParams>(), default))
            .ReturnsAsync(new SessionResumeResponse());

        var sut = new ChatService(acpClient.Object, errorLogger.Object, sessionManager);

        await sut.ResumeSessionAsync(new SessionResumeParams("remote-1", Environment.CurrentDirectory));

        var session = sessionManager.GetSession("remote-1");
        Assert.NotNull(session);
        Assert.Single(session!.History);
        Assert.Equal("cached", ((TextContentBlock)session.History[0].Content!).Text);

        sut.Dispose();
    }

    [Fact]
    public async Task CloseSessionAsync_WhenClosingCurrentTrackedSession_RemovesLocalSessionAndClearsCurrentSession()
    {
        var acpClient = new Mock<IAcpClient>(MockBehavior.Strict);
        var errorLogger = new Mock<IErrorLogger>(MockBehavior.Loose);
        var sessionManager = new SessionManager();

        acpClient.SetupGet(c => c.IsInitialized).Returns(true);
        acpClient.SetupGet(c => c.IsConnected).Returns(true);
        acpClient.SetupGet(c => c.AgentInfo).Returns((AgentInfo?)null);
        acpClient.SetupGet(c => c.AgentCapabilities).Returns((AgentCapabilities?)null);
        acpClient
            .Setup(c => c.ResumeSessionAsync(It.IsAny<SessionResumeParams>(), default))
            .ReturnsAsync(new SessionResumeResponse());
        acpClient
            .Setup(c => c.CloseSessionAsync(
                It.Is<SessionCloseParams>(p => p.SessionId == "remote-1"),
                default))
            .ReturnsAsync(SessionCloseResponse.Completed);

        var sut = new ChatService(acpClient.Object, errorLogger.Object, sessionManager);

        await sut.ResumeSessionAsync(new SessionResumeParams("remote-1", Environment.CurrentDirectory));
        Assert.Equal("remote-1", sut.CurrentSessionId);
        Assert.NotNull(sessionManager.GetSession("remote-1"));

        await sut.CloseSessionAsync(new SessionCloseParams("remote-1"));

        Assert.Null(sut.CurrentSessionId);
        Assert.Null(sessionManager.GetSession("remote-1"));

        sut.Dispose();
    }

    [Fact]
    public async Task CloseSessionAsync_WhenClosingNonCurrentTrackedSession_PreservesCurrentSessionAndRemovesClosedSession()
    {
        var acpClient = new Mock<IAcpClient>(MockBehavior.Strict);
        var errorLogger = new Mock<IErrorLogger>(MockBehavior.Loose);
        var sessionManager = new SessionManager();

        acpClient.SetupGet(c => c.IsInitialized).Returns(true);
        acpClient.SetupGet(c => c.IsConnected).Returns(true);
        acpClient.SetupGet(c => c.AgentInfo).Returns((AgentInfo?)null);
        acpClient.SetupGet(c => c.AgentCapabilities).Returns((AgentCapabilities?)null);
        acpClient
            .Setup(c => c.ResumeSessionAsync(
                It.Is<SessionResumeParams>(p => p.SessionId == "remote-1"),
                default))
            .ReturnsAsync(new SessionResumeResponse());
        acpClient
            .Setup(c => c.CloseSessionAsync(
                It.Is<SessionCloseParams>(p => p.SessionId == "remote-2"),
                default))
            .ReturnsAsync(SessionCloseResponse.Completed);

        await sessionManager.CreateSessionAsync("remote-2", cwd: Environment.CurrentDirectory);

        var sut = new ChatService(acpClient.Object, errorLogger.Object, sessionManager);

        await sut.ResumeSessionAsync(new SessionResumeParams("remote-1", Environment.CurrentDirectory));
        Assert.Equal("remote-1", sut.CurrentSessionId);
        Assert.NotNull(sessionManager.GetSession("remote-2"));

        await sut.CloseSessionAsync(new SessionCloseParams("remote-2"));

        Assert.Equal("remote-1", sut.CurrentSessionId);
        Assert.NotNull(sessionManager.GetSession("remote-1"));
        Assert.Null(sessionManager.GetSession("remote-2"));

        sut.Dispose();
    }

    [Fact]
    public void SessionUpdate_CurrentModeUpdate_UsesNormalizedModeIdForLegacyPayload()
    {
        var acpClient = new Mock<IAcpClient>(MockBehavior.Loose);
        var errorLogger = new Mock<IErrorLogger>(MockBehavior.Loose);
        var sessionManager = new SessionManager();

        var sut = new ChatService(acpClient.Object, errorLogger.Object, sessionManager);

        acpClient.Raise(
            client => client.SessionUpdateReceived += null,
            new SessionUpdateEventArgs("s1", new CurrentModeUpdate
            {
                LegacyModeId = "legacy-mode",
                Title = "Legacy mode"
            }));

        Assert.Equal("legacy-mode", sut.CurrentMode?.CurrentModeId);
        var session = sessionManager.GetSession("s1");
        Assert.NotNull(session);
        Assert.Equal("legacy-mode", session!.History.Single().ModeId);

        sut.Dispose();
    }

    [Fact]
    public async Task ChatServiceFactory_CreateChatService_UsesSharedSessionManagerForWarmLoadedPrompt()
    {
        var transport = new ScriptedTransport();
        var transportFactory = new Mock<ITransportFactory>(MockBehavior.Strict);
        var errorLogger = new Mock<IErrorLogger>(MockBehavior.Loose);
        var sessionManager = new SessionManager();
        var acpClient = new ScriptedAcpClient(sessionManager);

        transportFactory
            .Setup(factory => factory.CreateTransport(TransportType.Stdio, "agent", null, null))
            .Returns(transport);

        var sut = new ChatServiceFactory(
            transportFactory.Object,
            errorLogger.Object,
            sessionManager,
            new StubAcpClientFactory(acpClient),
            new LoggerConfiguration().CreateLogger());

        var chatService = sut.CreateChatService(TransportType.Stdio, "agent");

        await chatService.InitializeAsync(new InitializeParams(new ClientInfo("Test", "1.0.0"), new ClientCapabilities()));
        await chatService.LoadSessionAsync(new SessionLoadParams("remote-1", Environment.CurrentDirectory));
        var promptResponse = await chatService.SendPromptAsync(
            new SessionPromptParams("remote-1", new List<ContentBlock> { new TextContentBlock("hello") }));

        Assert.Equal(StopReason.EndTurn, promptResponse.StopReason);
        Assert.NotNull(sessionManager.GetSession("remote-1"));
        Assert.Same(transport, acpClient.CreatedForTransport);
    }

    private sealed class StubAcpClientFactory(ScriptedAcpClient client) : IAcpClientFactory
    {
        public IAcpClient CreateClient(ITransport transport)
        {
            client.CreatedForTransport = transport;
            return client;
        }
    }

    private sealed class ScriptedAcpClient(SessionManager sessionManager) : IAcpClient
    {
        public event EventHandler<InitializeResponse>? Initialized;
        public event EventHandler<SessionUpdateEventArgs>? SessionUpdateReceived;
        public event EventHandler<PermissionRequestEventArgs>? PermissionRequestReceived;
        public event EventHandler<FileSystemRequestEventArgs>? FileSystemRequestReceived;
        public event EventHandler<TerminalRequestEventArgs>? TerminalRequestReceived;
        public event EventHandler<TerminalStateChangedEventArgs>? TerminalStateChangedReceived;
        public event EventHandler<AskUserRequestEventArgs>? AskUserRequestReceived;
        public event EventHandler<string>? ErrorOccurred;

        public bool IsInitialized { get; private set; }
        public bool IsConnected => true;
        public AgentInfo? AgentInfo { get; private set; }
        public AgentCapabilities? AgentCapabilities { get; private set; }
        public ITransport? CreatedForTransport { get; set; }

        public Task<InitializeResponse> InitializeAsync(InitializeParams @params, CancellationToken cancellationToken = default)
        {
            IsInitialized = true;
            AgentInfo = new AgentInfo("TestAgent", "1.0.0");
            AgentCapabilities = new AgentCapabilities(loadSession: true);
            var response = new InitializeResponse(1, AgentInfo, AgentCapabilities);
            Initialized?.Invoke(this, response);
            return Task.FromResult(response);
        }

        public Task<SessionNewResponse> CreateSessionAsync(SessionNewParams @params, CancellationToken cancellationToken = default)
            => Task.FromResult(new SessionNewResponse { SessionId = "remote-1" });

        public async Task<SessionLoadResponse> LoadSessionAsync(SessionLoadParams @params, CancellationToken cancellationToken = default)
        {
            if (sessionManager.GetSession(@params.SessionId) == null)
            {
                await sessionManager.CreateSessionAsync(@params.SessionId, @params.Cwd).ConfigureAwait(false);
            }

            return SessionLoadResponse.Completed;
        }

        public Task<SessionResumeResponse> ResumeSessionAsync(SessionResumeParams @params, CancellationToken cancellationToken = default)
            => Task.FromResult(new SessionResumeResponse());

        public Task<SessionCloseResponse> CloseSessionAsync(SessionCloseParams @params, CancellationToken cancellationToken = default)
            => Task.FromResult(new SessionCloseResponse());

        public Task<SessionListResponse> ListSessionsAsync(SessionListParams @params, CancellationToken cancellationToken = default)
            => Task.FromResult(new SessionListResponse());

        public Task<SessionPromptResponse> SendPromptAsync(SessionPromptParams @params, CancellationToken cancellationToken = default)
            => Task.FromResult(new SessionPromptResponse(StopReason.EndTurn));

        public Task<SessionSetModeResponse> SetSessionModeAsync(SessionSetModeParams @params, CancellationToken cancellationToken = default)
            => Task.FromResult(new SessionSetModeResponse());

        public Task<SessionSetConfigOptionResponse> SetSessionConfigOptionAsync(SessionSetConfigOptionParams @params, CancellationToken cancellationToken = default)
            => Task.FromResult(new SessionSetConfigOptionResponse());

        public Task<SessionCancelResponse> CancelSessionAsync(SessionCancelParams @params, CancellationToken cancellationToken = default)
            => Task.FromResult(new SessionCancelResponse());

        public Task<AuthenticateResponse> AuthenticateAsync(AuthenticateParams @params, CancellationToken cancellationToken = default)
            => Task.FromResult(new AuthenticateResponse(true));

        public Task<bool> RespondToPermissionRequestAsync(object messageId, string outcome, string? optionId = null)
            => Task.FromResult(true);

        public Task<bool> RespondToFileSystemRequestAsync(object messageId, bool success, string? content = null, string? message = null)
            => Task.FromResult(true);

        public Task<bool> RespondToAskUserRequestAsync(object messageId, IReadOnlyDictionary<string, string> answers)
            => Task.FromResult(true);

        public Task<bool> DisconnectAsync()
        {
            _ = SessionUpdateReceived;
            _ = PermissionRequestReceived;
            _ = FileSystemRequestReceived;
            _ = TerminalRequestReceived;
            _ = TerminalStateChangedReceived;
            _ = AskUserRequestReceived;
            _ = ErrorOccurred;
            return Task.FromResult(true);
        }
    }

    private sealed class ScriptedTransport : ITransport
    {
        private readonly MessageParser _parser = new();
        private int _nextResponseId = 1;

        public event EventHandler<MessageReceivedEventArgs>? MessageReceived;

        public event EventHandler<TransportErrorEventArgs>? ErrorOccurred;

        public bool IsConnected => true;

        public List<string> SentMessages { get; } = [];

        public Task<bool> ConnectAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<bool> DisconnectAsync() => Task.FromResult(true);

        public Task<bool> SendMessageAsync(string message, CancellationToken cancellationToken = default)
        {
            _ = ErrorOccurred;
            SentMessages.Add(message);

            var parsed = _parser.ParseMessage(message);
            if (parsed is JsonRpcRequest request)
            {
                var response = request.Method switch
                {
                    "initialize" => new JsonRpcResponse(
                        request.Id,
                        JsonSerializer.SerializeToElement(
                            new InitializeResponse(
                                1,
                                new AgentInfo("TestAgent", "1.0.0"),
                                new AgentCapabilities(loadSession: true)),
                            _parser.Options)),
                    "session/load" => new JsonRpcResponse(
                        request.Id,
                        JsonSerializer.SerializeToElement(new SessionLoadResponse(), _parser.Options)),
                    "session/prompt" => new JsonRpcResponse(
                        request.Id,
                        JsonSerializer.SerializeToElement(new SessionPromptResponse(StopReason.EndTurn), _parser.Options)),
                    _ => new JsonRpcResponse(
                        request.Id ?? _nextResponseId++,
                        JsonSerializer.SerializeToElement(new { }, _parser.Options))
                };

                MessageReceived?.Invoke(this, new MessageReceivedEventArgs(_parser.SerializeMessage(response)));
            }

            return Task.FromResult(true);
        }
    }
}
