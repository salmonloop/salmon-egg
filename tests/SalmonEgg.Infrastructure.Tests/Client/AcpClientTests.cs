using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using SalmonEgg.Infrastructure.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Moq;
using SalmonEgg.Domain.Interfaces.Transport;
using SalmonEgg.Domain.Models.Content;
using SalmonEgg.Domain.Models.JsonRpc;
using SalmonEgg.Domain.Models.Session;
using SalmonEgg.Domain.Models.Protocol;
using SalmonEgg.Domain.Models.Mcp;
using SalmonEgg.Domain.Services;
using SalmonEgg.Infrastructure.Client;
using SalmonEgg.Infrastructure.Services;
using Xunit;
using SalmonEgg.Domain.Interfaces;

namespace SalmonEgg.Infrastructure.Tests.Client
{
    public class AcpClientTests
    {
        private static readonly string AbsoluteCwd = Path.GetFullPath(Path.Combine(
            Path.GetTempPath(),
            "salmon-egg-tests",
            "workspace",
            "project"));
        private readonly Mock<ITransport> _transportMock = new();
        private readonly Mock<IMessageParser> _parserMock = new();
        private readonly Mock<IErrorLogger> _errorLoggerMock = new();

        public AcpClientTests()
        {
            _transportMock.SetupGet(t => t.IsConnected).Returns(true);
            _parserMock.Setup(p => p.Options).Returns(new JsonSerializerOptions());
        }

        private async Task<AcpClient> CreateInitializedClientAsync(
            AgentCapabilities? capabilities = null,
            ClientCapabilities? clientCapabilities = null,
            ITerminalSessionManager? terminalSessionManager = null,
            ISessionManager? sessionManager = null)
        {
            var parser = new MessageParser(); // Use real parser for serialization

            var client = new AcpClient(
                _transportMock.Object,
                parser,
                null,
                _errorLoggerMock.Object,
                sessionManager: sessionManager,
                terminalSessionManager: terminalSessionManager);

            // Mock InitializeAsync response
            var initResponse = new InitializeResponse(
                1, // protocolVersion
                new AgentInfo("TestAgent", "1.0.0"),
                capabilities ?? new AgentCapabilities(loadSession: true)
            );

            SetupJsonRpcResponse(
                "initialize",
                JsonSerializer.SerializeToElement(initResponse, parser.Options),
                parser);

            await client.InitializeAsync(new InitializeParams(
                new ClientInfo("Test", "1.0.0"),
                clientCapabilities ?? new ClientCapabilities()));

            return client;
        }


        [Fact]
        public async Task AuthenticateAsync_WhenAgentReturnsEmptyObject_ShouldComplete()
        {
            var parser = new MessageParser();
            var client = await CreateInitializedClientAsync();

            SetupJsonRpcResponse(
                "authenticate",
                ElementFromJson("{}"),
                parser);

            var result = await client.AuthenticateAsync(new AuthenticateParams("agent-login"));

            Assert.NotNull(result);
        }

        [Fact]
        public async Task CreateSessionAsync_SlowButValidResponse_CompletesWhenResponseEventuallyArrives()
        {
            var parser = new MessageParser();
            var client = await CreateInitializedClientAsync(
                new AgentCapabilities(loadSession: true));

            SetupJsonRpcResponse(
                "session/new",
                JsonSerializer.SerializeToElement(new SessionNewResponse("session-123"), parser.Options),
                parser,
                responseDelay: TimeSpan.FromMilliseconds(200));

            var result = await client.CreateSessionAsync(new SessionNewParams(AbsoluteCwd, null));
            Assert.Equal("session-123", result.SessionId);
        }

        [Fact]
        public async Task CreateSessionAsync_WhenSessionAlreadyTrackedFromUpdate_ShouldReturnResponse()
        {
            var parser = new MessageParser();
            var sessionManager = new SessionManager();
            var client = await CreateInitializedClientAsync(sessionManager: sessionManager);
            await sessionManager.CreateSessionAsync("session-123", AbsoluteCwd);

            SetupJsonRpcResponse(
                "session/new",
                JsonSerializer.SerializeToElement(new SessionNewResponse("session-123"), parser.Options),
                parser);

            var result = await client.CreateSessionAsync(new SessionNewParams(AbsoluteCwd, null));

            Assert.Equal("session-123", result.SessionId);
            Assert.NotNull(sessionManager.GetSession("session-123"));
        }

        [Fact]
        public async Task CreateSessionAsync_WhenCwdIsRelative_ThrowsInvalidParams()
        {
            var client = await CreateInitializedClientAsync();

            var ex = await Assert.ThrowsAsync<AcpException>(() =>
                client.CreateSessionAsync(new SessionNewParams("relative-path", null)));

            Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.ErrorCode);
            _transportMock.Verify(
                t => t.SendMessageAsync(It.IsRegex("session/new"), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task CreateSessionAsync_WhenHttpMcpServerUnsupported_DoesNotSendProtocolRequest()
        {
            var client = await CreateInitializedClientAsync(
                capabilities: new AgentCapabilities(mcpCapabilities: new McpCapabilities(http: false)));

            var ex = await Assert.ThrowsAsync<AcpException>(() =>
                client.CreateSessionAsync(new SessionNewParams(
                    AbsoluteCwd,
                    new List<McpServer> { new HttpMcpServer("api", "https://api.example.com/mcp") })));

            Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.ErrorCode);
            Assert.Contains("mcpCapabilities.http", ex.Message);
            _transportMock.Verify(
                t => t.SendMessageAsync(It.IsRegex("session/new"), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task CreateSessionAsync_WhenMcpServersIsNull_DoesNotSendProtocolRequest()
        {
            var client = await CreateInitializedClientAsync();
            var @params = new SessionNewParams(AbsoluteCwd)
            {
                McpServers = null!
            };

            var ex = await Assert.ThrowsAsync<AcpException>(() => client.CreateSessionAsync(@params));

            Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.ErrorCode);
            Assert.Contains("mcpServers", ex.Message);
            Assert.Contains("array", ex.Message);
            _transportMock.Verify(
                t => t.SendMessageAsync(It.IsRegex("session/new"), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task CreateSessionAsync_WhenHttpMcpServerUrlIsPresent_SendsProtocolRequest()
        {
            var parser = new MessageParser();
            var client = await CreateInitializedClientAsync(
                capabilities: new AgentCapabilities(mcpCapabilities: new McpCapabilities(http: true)));

            SetupJsonRpcResponse(
                "session/new",
                JsonSerializer.SerializeToElement(new SessionNewResponse("session-123"), parser.Options),
                parser);

            await client.CreateSessionAsync(new SessionNewParams(
                AbsoluteCwd,
                new List<McpServer> { new HttpMcpServer("api", "api.example.com/mcp") }));

            _transportMock.Verify(
                t => t.SendMessageAsync(It.IsRegex("session/new"), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task CreateSessionAsync_WhenNoMcpServers_SendsEmptyMcpServersArray()
        {
            var parser = new MessageParser();
            var sentMessages = new ConcurrentQueue<string>();
            var client = await CreateInitializedClientAsync();

            SetupJsonRpcResponse(
                "session/new",
                JsonSerializer.SerializeToElement(new SessionNewResponse("session-123"), parser.Options),
                parser,
                onSend: message => sentMessages.Enqueue(message));

            await client.CreateSessionAsync(new SessionNewParams(AbsoluteCwd));

            Assert.True(sentMessages.TryDequeue(out var requestJson));
            using var document = JsonDocument.Parse(requestJson);
            var @params = document.RootElement.GetProperty("params");
            Assert.Equal(JsonValueKind.Array, @params.GetProperty("mcpServers").ValueKind);
            Assert.Equal(0, @params.GetProperty("mcpServers").GetArrayLength());
        }

        [Fact]
        public async Task CreateSessionAsync_WhenStdioMcpServerCommandMissing_DoesNotSendProtocolRequest()
        {
            var client = await CreateInitializedClientAsync();

            var ex = await Assert.ThrowsAsync<AcpException>(() =>
                client.CreateSessionAsync(new SessionNewParams(
                    AbsoluteCwd,
                    new List<McpServer> { new StdioMcpServer("filesystem", string.Empty) })));

            Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.ErrorCode);
            Assert.Contains("requires a command", ex.Message);
            _transportMock.Verify(
                t => t.SendMessageAsync(It.IsRegex("session/new"), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task CreateSessionAsync_WhenStdioMcpServerCommandIsRelative_DoesNotSendProtocolRequest()
        {
            var client = await CreateInitializedClientAsync();

            var ex = await Assert.ThrowsAsync<AcpException>(() =>
                client.CreateSessionAsync(new SessionNewParams(
                    AbsoluteCwd,
                    new List<McpServer> { new StdioMcpServer("filesystem", "mcp-server") })));

            Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.ErrorCode);
            Assert.Contains("absolute command path", ex.Message);
            _transportMock.Verify(
                t => t.SendMessageAsync(It.IsRegex("session/new"), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task CreateSessionAsync_WhenHttpMcpServerSupported_SendsProtocolRequest()
        {
            var parser = new MessageParser();
            var sentMessages = new ConcurrentQueue<string>();
            var client = await CreateInitializedClientAsync(
                capabilities: new AgentCapabilities(mcpCapabilities: new McpCapabilities(http: true)));

            SetupJsonRpcResponse(
                "session/new",
                JsonSerializer.SerializeToElement(new SessionNewResponse("session-123"), parser.Options),
                parser,
                onSend: message => sentMessages.Enqueue(message));

            var result = await client.CreateSessionAsync(new SessionNewParams(
                AbsoluteCwd,
                new List<McpServer> { new HttpMcpServer("api", "https://api.example.com/mcp") }));

            Assert.Equal("session-123", result.SessionId);
            Assert.True(sentMessages.TryDequeue(out var requestJson));

            using var document = JsonDocument.Parse(requestJson);
            var mcpServers = document.RootElement.GetProperty("params").GetProperty("mcpServers");
            Assert.Equal("http", mcpServers[0].GetProperty("type").GetString());
        }

        [Fact]
        public async Task InitializeAsync_SendsAskUserCapabilityMetadataInClientCapabilities()
        {
            var parser = new MessageParser();
            var client = new AcpClient(_transportMock.Object, parser, null, _errorLoggerMock.Object);
            string? sentInitialize = null;

            var initResponse = new InitializeResponse(
                1,
                new AgentInfo("TestAgent", "1.0.0"),
                new AgentCapabilities());

            SetupJsonRpcResponse(
                "initialize",
                JsonSerializer.SerializeToElement(initResponse, parser.Options),
                parser,
                onSend: message => sentInitialize = message);

            await client.InitializeAsync(new InitializeParams(
                new ClientInfo("Test", "1.0.0"),
                ClientCapabilityDefaults.Create()));

            Assert.NotNull(sentInitialize);

            using var document = JsonDocument.Parse(sentInitialize!);
            var clientCapabilities = document.RootElement
                .GetProperty("params")
                .GetProperty("clientCapabilities");
            var meta = clientCapabilities.GetProperty("_meta");
            var extensions = meta.GetProperty(ClientCapabilityMetadata.ExtensionsMetaKey);

            Assert.True(extensions.GetProperty(ClientCapabilityMetadata.AskUserExtensionMethod).GetBoolean());
            Assert.False(extensions.TryGetProperty("interaction.ask_user", out _));
            Assert.False(clientCapabilities.TryGetProperty("fs", out _));
            Assert.False(clientCapabilities.TryGetProperty("terminal", out _));
        }

        [Fact]
        public async Task InitializeAsync_WhenServerProtocolIsOlder_UsesCompatibilityMode()
        {
            var parser = new MessageParser();
            var client = new AcpClient(_transportMock.Object, parser, null, _errorLoggerMock.Object);

            var initResponse = new InitializeResponse(
                0,
                new AgentInfo("TestAgent", "1.0.0"),
                new AgentCapabilities());

            SetupJsonRpcResponse(
                "initialize",
                JsonSerializer.SerializeToElement(initResponse, parser.Options),
                parser);

            var response = await client.InitializeAsync(new InitializeParams(
                new ClientInfo("Test", "1.0.0"),
                ClientCapabilityDefaults.Create()));

            Assert.True(client.IsInitialized);
            Assert.Equal(0, response.ProtocolVersion);
        }

        [Fact]
        public async Task InitializeAsync_WhenDisconnectedBeforeResponse_CancelsPendingInitialize()
        {
            var parser = new MessageParser();
            var client = new AcpClient(_transportMock.Object, parser, null, _errorLoggerMock.Object);
            var initializeSent = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

            _transportMock
                .Setup(t => t.SendMessageAsync(It.IsRegex("initialize"), It.IsAny<CancellationToken>()))
                .Callback(() => initializeSent.TrySetResult(null))
                .ReturnsAsync(true);
            _transportMock
                .Setup(t => t.DisconnectAsync())
                .ReturnsAsync(true);

            using var cts = new CancellationTokenSource();
            var initializeTask = client.InitializeAsync(
                new InitializeParams(
                    new ClientInfo("Test", "1.0.0"),
                    ClientCapabilityDefaults.Create()),
                cts.Token);

            try
            {
                await initializeSent.Task.WaitAsync(TimeSpan.FromSeconds(2));

                await client.DisconnectAsync();

                await Assert.ThrowsAnyAsync<OperationCanceledException>(
                    async () => await initializeTask.WaitAsync(TimeSpan.FromSeconds(2)));
            }
            finally
            {
                cts.Cancel();
                try
                {
                    await initializeTask.WaitAsync(TimeSpan.FromSeconds(2));
                }
                catch
                {
                }
            }
        }

        [Fact]
        public async Task InitializeAsync_WhenTransportConnectReturnsFalse_IncludesLastTransportError()
        {
            var parser = new MessageParser();
            _transportMock.SetupGet(t => t.IsConnected).Returns(false);
            _transportMock
                .Setup(t => t.ConnectAsync(It.IsAny<CancellationToken>()))
                .Callback(() => _transportMock.Raise(
                    t => t.ErrorOccurred += null,
                    new TransportErrorEventArgs("无法启动进程：stdio command not found")))
                .ReturnsAsync(false);
            var client = new AcpClient(_transportMock.Object, parser, null, _errorLoggerMock.Object);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                client.InitializeAsync(new InitializeParams(
                    new ClientInfo("Test", "1.0.0"),
                    ClientCapabilityDefaults.Create())));

            Assert.Contains("无法启动进程：stdio command not found", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task InitializeAsync_WhenTransportConnectErrorHasException_IncludesRawExceptionMessage()
        {
            var parser = new MessageParser();
            _transportMock.SetupGet(t => t.IsConnected).Returns(false);
            _transportMock
                .Setup(t => t.ConnectAsync(It.IsAny<CancellationToken>()))
                .Callback(() => _transportMock.Raise(
                    t => t.ErrorOccurred += null,
                    new TransportErrorEventArgs(
                        "Failed to connect transport",
                        new InvalidOperationException(
                            "Failed to construct 'WebSocket': An insecure WebSocket connection may not be initiated from a page loaded over HTTPS."))))
                .ReturnsAsync(false);
            var client = new AcpClient(_transportMock.Object, parser, null, _errorLoggerMock.Object);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                client.InitializeAsync(new InitializeParams(
                    new ClientInfo("Test", "1.0.0"),
                    ClientCapabilityDefaults.Create())));

            Assert.Contains("Failed to connect transport", ex.Message, StringComparison.Ordinal);
            Assert.Contains("insecure WebSocket connection", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("HTTPS", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task InitializeAsync_WhenTransportDisconnectsBeforeResponse_IncludesTransportError()
        {
            var parser = new MessageParser();
            var isConnected = true;
            _transportMock.SetupGet(t => t.IsConnected).Returns(() => isConnected);
            var client = new AcpClient(_transportMock.Object, parser, null, _errorLoggerMock.Object);
            var initializeSent = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

            _transportMock
                .Setup(t => t.SendMessageAsync(It.IsRegex("initialize"), It.IsAny<CancellationToken>()))
                .Callback(() => initializeSent.TrySetResult(null))
                .ReturnsAsync(true);

            using var cts = new CancellationTokenSource();
            var initializeTask = client.InitializeAsync(
                new InitializeParams(
                    new ClientInfo("Test", "1.0.0"),
                    ClientCapabilityDefaults.Create()),
                cts.Token);

            try
            {
                await initializeSent.Task.WaitAsync(TimeSpan.FromSeconds(2));
                isConnected = false;
                _transportMock.Raise(
                    t => t.ErrorOccurred += null,
                    new TransportErrorEventArgs("Agent 进程已退出"));

                var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await initializeTask.WaitAsync(TimeSpan.FromSeconds(2)));
                Assert.Contains("Agent 进程已退出", ex.Message, StringComparison.Ordinal);
            }
            finally
            {
                cts.Cancel();
                try
                {
                    await initializeTask.WaitAsync(TimeSpan.FromSeconds(2));
                }
                catch
                {
                }
            }
        }

        [Fact]
        public async Task CreateSessionAsync_WhenTransportSendReturnsFalse_IncludesTransportError()
        {
            var parser = new MessageParser();
            var client = await CreateInitializedClientAsync();
            var sessionNewSent = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

            _transportMock
                .Setup(t => t.SendMessageAsync(It.IsRegex("session/new"), It.IsAny<CancellationToken>()))
                .Callback(() =>
                {
                    _transportMock.Raise(
                        t => t.ErrorOccurred += null,
                        new TransportErrorEventArgs("发送消息失败：broken pipe"));
                    sessionNewSent.TrySetResult(null);
                })
                .ReturnsAsync(false);

            using var cts = new CancellationTokenSource();
            var createTask = client.CreateSessionAsync(new SessionNewParams(AbsoluteCwd, null), cts.Token);

            try
            {
                await sessionNewSent.Task.WaitAsync(TimeSpan.FromSeconds(2));
                var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await createTask.WaitAsync(TimeSpan.FromSeconds(2)));
                Assert.Contains("发送消息失败：broken pipe", ex.Message, StringComparison.Ordinal);
            }
            finally
            {
                cts.Cancel();
                try
                {
                    await createTask.WaitAsync(TimeSpan.FromSeconds(2));
                }
                catch
                {
                }
            }
        }

        [Fact]
        public async Task InitializeAsync_WhenServerProtocolIsNewer_ThrowsProtocolVersionMismatch()
        {
            var parser = new MessageParser();
            var client = new AcpClient(_transportMock.Object, parser, null, _errorLoggerMock.Object);

            _transportMock
                .Setup(t => t.SendMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns<string, CancellationToken>((_, _) =>
                {
                    var response = new JsonRpcResponse(
                        1,
                        JsonSerializer.SerializeToElement(
                            new InitializeResponse(
                                2,
                                new AgentInfo("TestAgent", "1.0.0"),
                                new AgentCapabilities()),
                            parser.Options));
                    _transportMock.Raise(
                        t => t.MessageReceived += null,
                        new MessageReceivedEventArgs(parser.SerializeMessage(response)));
                    return Task.FromResult(true);
                });

            var ex = await Assert.ThrowsAsync<AcpException>(() => client.InitializeAsync(new InitializeParams(
                new ClientInfo("Test", "1.0.0"),
                ClientCapabilityDefaults.Create())));

            Assert.Equal(JsonRpcErrorCode.ProtocolVersionMismatch, ex.ErrorCode);
        }

        [Fact]
        public void TransportErrors_ForStdioBridgeFailures_ShouldAppendSshGuidance()
        {
            var parser = new MessageParser();
            var client = new AcpClient(_transportMock.Object, parser, null, _errorLoggerMock.Object);
            string? receivedError = null;
            client.ErrorOccurred += (_, error) => receivedError = error;

            _transportMock.Raise(
                t => t.ErrorOccurred += null,
                new TransportErrorEventArgs("进程启动后立即退出，退出码=255"));

            Assert.NotNull(receivedError);
            Assert.Contains("ssh -t", receivedError, StringComparison.Ordinal);
            Assert.Contains("stdout", receivedError, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("BatchMode=yes", receivedError, StringComparison.Ordinal);
        }

        [Fact]
        public async Task LoadSessionAsync_SlowButValidResponse_CompletesWhenResponseEventuallyArrives()
        {
            var parser = new MessageParser();
            var client = await CreateInitializedClientAsync();

            SetupJsonRpcResponse(
                "session/load",
                ElementFromJson("{}"),
                parser,
                responseDelay: TimeSpan.FromMilliseconds(200));

            var result = await client.LoadSessionAsync(new SessionLoadParams("session-123", AbsoluteCwd, null));

            Assert.NotNull(result);
        }

        [Fact]
        public async Task LoadSessionAsync_WhenReplayTrafficContinues_CompletesWhenResponseEventuallyArrives()
        {
            var parser = new MessageParser();
            var client = await CreateInitializedClientAsync();

            _transportMock
                .Setup(t => t.SendMessageAsync(It.IsRegex("session/load"), It.IsAny<CancellationToken>()))
                .Returns<string, CancellationToken>((message, cancellationToken) =>
                {
                    var request = parser.ParseRequest(message);
                    var response = new JsonRpcResponse(request.Id, ElementFromJson("{}"));
                    var replayUpdate = new JsonRpcNotification(
                        "session/update",
                        JsonSerializer.SerializeToElement(
                            new SessionUpdateParams(
                                "session-123",
                                new AgentMessageUpdate(new TextContentBlock("replay chunk"))),
                            parser.Options));

                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(30, cancellationToken);
                        RaiseTransportMessage(parser.SerializeMessage(replayUpdate));
                        await Task.Delay(40, cancellationToken);
                        RaiseTransportMessage(parser.SerializeMessage(response));
                    }, cancellationToken);

                    return Task.FromResult(true);
                });

            var result = await client.LoadSessionAsync(new SessionLoadParams("session-123", AbsoluteCwd, null));

            Assert.NotNull(result);
        }

        [Fact]
        public async Task LoadSessionAsync_WhenCallerCancels_ThrowsOperationCanceledException()
        {
            var client = await CreateInitializedClientAsync();

            _transportMock.Setup(t => t.SendMessageAsync(It.IsRegex("session/load"), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            using var cts = new CancellationTokenSource();
            var loadTask = client.LoadSessionAsync(
                new SessionLoadParams("session-123", AbsoluteCwd, null),
                cts.Token);

            await Task.Delay(50);
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => loadTask);
        }

        [Fact]
        public async Task LoadSessionAsync_WhenCwdIsRelative_ThrowsInvalidParams()
        {
            var client = await CreateInitializedClientAsync(
                capabilities: new AgentCapabilities(loadSession: true));

            var ex = await Assert.ThrowsAsync<AcpException>(() =>
                client.LoadSessionAsync(new SessionLoadParams("session-123", "relative-path", null)));

            Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.ErrorCode);
            _transportMock.Verify(
                t => t.SendMessageAsync(It.IsRegex("session/load"), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task LoadSessionAsync_WhenHttpMcpServerUnsupported_DoesNotSendProtocolRequest()
        {
            var client = await CreateInitializedClientAsync(
                capabilities: new AgentCapabilities(loadSession: true));

            var ex = await Assert.ThrowsAsync<AcpException>(() =>
                client.LoadSessionAsync(new SessionLoadParams(
                    "session-123",
                    AbsoluteCwd,
                    new List<McpServer> { new HttpMcpServer("api", "https://api.example.com/mcp") })));

            Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.ErrorCode);
            Assert.Contains("mcpCapabilities.http", ex.Message);
            _transportMock.Verify(
                t => t.SendMessageAsync(It.IsRegex("session/load"), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task LoadSessionAsync_WhenMcpServersIsNull_DoesNotSendProtocolRequest()
        {
            var client = await CreateInitializedClientAsync(
                capabilities: new AgentCapabilities(loadSession: true));
            var @params = new SessionLoadParams("session-123", AbsoluteCwd)
            {
                McpServers = null!
            };

            var ex = await Assert.ThrowsAsync<AcpException>(() => client.LoadSessionAsync(@params));

            Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.ErrorCode);
            Assert.Contains("mcpServers", ex.Message);
            Assert.Contains("array", ex.Message);
            _transportMock.Verify(
                t => t.SendMessageAsync(It.IsRegex("session/load"), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task LoadSessionAsync_WhenAgentDoesNotSupportLoadSession_DoesNotSendProtocolRequest()
        {
            var client = await CreateInitializedClientAsync(
                capabilities: new AgentCapabilities(loadSession: false));

            var result = await client.LoadSessionAsync(new SessionLoadParams("session-123", AbsoluteCwd, null));

            Assert.Same(SessionLoadResponse.Completed, result);
            _transportMock.Verify(
                t => t.SendMessageAsync(It.IsRegex("session/load"), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task LoadSessionAsync_WhenAgentDoesNotSupportLoadSession_DoesNotValidateMcpServers()
        {
            var client = await CreateInitializedClientAsync(
                capabilities: new AgentCapabilities(loadSession: false));

            var result = await client.LoadSessionAsync(new SessionLoadParams(
                "session-123",
                AbsoluteCwd,
                new List<McpServer> { new HttpMcpServer("api", "https://api.example.com/mcp") }));

            Assert.Same(SessionLoadResponse.Completed, result);
            _transportMock.Verify(
                t => t.SendMessageAsync(It.IsRegex("session/load"), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task LoadSessionAsync_WhenAgentDoesNotAdvertiseLoadSession_DoesNotSendProtocolRequest()
        {
            var client = await CreateInitializedClientAsync(
                capabilities: new AgentCapabilities());

            var result = await client.LoadSessionAsync(new SessionLoadParams("session-123", AbsoluteCwd, null));

            Assert.Same(SessionLoadResponse.Completed, result);
            _transportMock.Verify(
                t => t.SendMessageAsync(It.IsRegex("session/load"), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task LoadSessionAsync_WhenResultIsNull_CompletesWithoutParseFailure()
        {
            var parser = new MessageParser();
            var client = await CreateInitializedClientAsync();

            SetupJsonRpcResponse(
                "session/load",
                JsonSerializer.SerializeToElement<object?>(null, parser.Options),
                parser);

            var result = await client.LoadSessionAsync(new SessionLoadParams("session-123", AbsoluteCwd, null));

            Assert.Same(SessionLoadResponse.Completed, result);
        }

        [Fact]
        public async Task LoadSessionAsync_WhenNoMcpServers_SendsEmptyMcpServersArray()
        {
            var parser = new MessageParser();
            var sentMessages = new ConcurrentQueue<string>();
            var client = await CreateInitializedClientAsync();

            SetupJsonRpcResponse(
                "session/load",
                JsonSerializer.SerializeToElement<object?>(null, parser.Options),
                parser,
                onSend: message => sentMessages.Enqueue(message));

            await client.LoadSessionAsync(new SessionLoadParams("session-123", AbsoluteCwd));

            Assert.True(sentMessages.TryDequeue(out var requestJson));
            using var document = JsonDocument.Parse(requestJson);
            var @params = document.RootElement.GetProperty("params");
            Assert.Equal(JsonValueKind.Array, @params.GetProperty("mcpServers").ValueKind);
            Assert.Equal(0, @params.GetProperty("mcpServers").GetArrayLength());
        }

        [Fact]
        public async Task LoadSessionAsync_ParsesModesAndConfigOptionsFromResponse()
        {
            var parser = new MessageParser();
            var client = await CreateInitializedClientAsync();

            SetupJsonRpcResponse(
                "session/load",
                ElementFromJson(
                    """
                    {
                      "modes": {
                        "currentModeId": "plan",
                        "availableModes": [
                          {
                            "id": "plan",
                            "name": "Plan",
                            "description": "Planning mode"
                          }
                        ]
                      },
                      "configOptions": [
                        {
                          "id": "mode",
                          "name": "Mode",
                          "category": "mode",
                          "type": "string",
                          "currentValue": "plan",
                          "options": [
                            {
                              "value": "plan",
                              "name": "Plan",
                              "description": "Planning mode"
                            }
                          ]
                        }
                      ]
                    }
                    """),
                parser);

            var result = await client.LoadSessionAsync(new SessionLoadParams("session-123", AbsoluteCwd, null));

            Assert.NotNull(result.Modes);
            Assert.Equal("plan", result.Modes!.CurrentModeId);
            Assert.Single(result.Modes.AvailableModes);
            Assert.Equal("plan", result.Modes.AvailableModes[0].Id);

            Assert.NotNull(result.ConfigOptions);
            Assert.Single(result.ConfigOptions!);
            Assert.Equal("mode", result.ConfigOptions![0].Id);
            Assert.Equal("plan", result.ConfigOptions[0].CurrentValue);
        }

        [Fact]
        public async Task LoadSessionAsync_WhenPayloadHasNoModesOrConfigOptions_TreatsResponseAsCompatibleEmptyResult()
        {
            var parser = new MessageParser();
            var client = await CreateInitializedClientAsync();

            SetupJsonRpcResponse("session/load", ElementFromJson("{}"), parser);

            var result = await client.LoadSessionAsync(new SessionLoadParams("session-123", AbsoluteCwd, null));

            Assert.NotNull(result);
            Assert.Null(result.Modes);
            Assert.Null(result.ConfigOptions);
        }

        [Fact]
        public async Task ResumeSessionAsync_WhenAgentDoesNotSupportSessionResume_DoesNotSendProtocolRequest()
        {
            var client = await CreateInitializedClientAsync(
                capabilities: new AgentCapabilities(loadSession: true));

            var result = await client.ResumeSessionAsync(new SessionResumeParams("session-123", AbsoluteCwd));

            Assert.Same(SessionResumeResponse.Completed, result);
            _transportMock.Verify(
                t => t.SendMessageAsync(It.IsRegex("session/resume"), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task ResumeSessionAsync_WhenAgentDoesNotSupportSessionResume_DoesNotValidateMcpServers()
        {
            var client = await CreateInitializedClientAsync(
                capabilities: new AgentCapabilities(loadSession: true));

            var result = await client.ResumeSessionAsync(new SessionResumeParams(
                "session-123",
                AbsoluteCwd,
                new List<McpServer> { new SseMcpServer("events", "https://events.example.com/mcp") }));

            Assert.Same(SessionResumeResponse.Completed, result);
            _transportMock.Verify(
                t => t.SendMessageAsync(It.IsRegex("session/resume"), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task ResumeSessionAsync_WhenCwdIsRelative_ThrowsInvalidParams()
        {
            var client = await CreateInitializedClientAsync(
                capabilities: new AgentCapabilities(sessionCapabilities: new SessionCapabilities
                {
                    Resume = new SessionResumeCapabilities()
                }));

            var ex = await Assert.ThrowsAsync<AcpException>(() =>
                client.ResumeSessionAsync(new SessionResumeParams("session-123", "relative-path")));

            Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.ErrorCode);
            _transportMock.Verify(
                t => t.SendMessageAsync(It.IsRegex("session/resume"), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task ResumeSessionAsync_WhenSseMcpServerUnsupported_DoesNotSendProtocolRequest()
        {
            var client = await CreateInitializedClientAsync(
                capabilities: new AgentCapabilities(sessionCapabilities: new SessionCapabilities
                {
                    Resume = new SessionResumeCapabilities()
                }));

            var ex = await Assert.ThrowsAsync<AcpException>(() =>
                client.ResumeSessionAsync(new SessionResumeParams(
                    "session-123",
                    AbsoluteCwd,
                    new List<McpServer> { new SseMcpServer("events", "https://events.example.com/mcp") })));

            Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.ErrorCode);
            Assert.Contains("mcpCapabilities.sse", ex.Message);
            _transportMock.Verify(
                t => t.SendMessageAsync(It.IsRegex("session/resume"), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task ResumeSessionAsync_WhenMcpServersIsNull_DoesNotSendProtocolRequest()
        {
            var client = await CreateInitializedClientAsync(
                capabilities: new AgentCapabilities(sessionCapabilities: new SessionCapabilities
                {
                    Resume = new SessionResumeCapabilities()
                }));
            var @params = new SessionResumeParams("session-123", AbsoluteCwd)
            {
                McpServers = null!
            };

            var ex = await Assert.ThrowsAsync<AcpException>(() => client.ResumeSessionAsync(@params));

            Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.ErrorCode);
            Assert.Contains("mcpServers", ex.Message);
            Assert.Contains("array", ex.Message);
            _transportMock.Verify(
                t => t.SendMessageAsync(It.IsRegex("session/resume"), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task ResumeSessionAsync_WhenSupported_SendsStandardSessionResumeAndParsesResponse()
        {
            var parser = new MessageParser();
            var sentMessages = new ConcurrentQueue<string>();
            var client = await CreateInitializedClientAsync(
                capabilities: new AgentCapabilities(sessionCapabilities: new SessionCapabilities
                {
                    Resume = new SessionResumeCapabilities()
                }));

            SetupJsonRpcResponse(
                "session/resume",
                ElementFromJson(
                    """
                    {
                      "modes": {
                        "currentModeId": "plan",
                        "availableModes": [
                          {
                            "id": "plan",
                            "name": "Plan"
                          }
                        ]
                      }
                    }
                    """),
                parser,
                onSend: message => sentMessages.Enqueue(message));

            var result = await client.ResumeSessionAsync(new SessionResumeParams("session-123", AbsoluteCwd));

            Assert.NotNull(result.Modes);
            Assert.Equal("plan", result.Modes!.CurrentModeId);
            Assert.True(sentMessages.TryDequeue(out var requestJson));

            using var document = JsonDocument.Parse(requestJson);
            Assert.Equal("session/resume", document.RootElement.GetProperty("method").GetString());
            var @params = document.RootElement.GetProperty("params");
            Assert.Equal("session-123", @params.GetProperty("sessionId").GetString());
            Assert.Equal(AbsoluteCwd, @params.GetProperty("cwd").GetString());
            Assert.Equal(JsonValueKind.Array, @params.GetProperty("mcpServers").ValueKind);
        }

        [Fact]
        public async Task CloseSessionAsync_WhenAgentDoesNotSupportSessionClose_DoesNotSendProtocolRequest()
        {
            var client = await CreateInitializedClientAsync(
                capabilities: new AgentCapabilities(loadSession: true));

            var result = await client.CloseSessionAsync(new SessionCloseParams("session-123"));

            Assert.Same(SessionCloseResponse.Completed, result);
            _transportMock.Verify(
                t => t.SendMessageAsync(It.IsRegex("session/close"), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task CloseSessionAsync_WhenSupported_SendsStandardSessionClose()
        {
            var parser = new MessageParser();
            var sentMessages = new ConcurrentQueue<string>();
            var client = await CreateInitializedClientAsync(
                capabilities: new AgentCapabilities(sessionCapabilities: new SessionCapabilities
                {
                    Close = new SessionCloseCapabilities()
                }));

            SetupJsonRpcResponse(
                "session/close",
                ElementFromJson("{}"),
                parser,
                onSend: message => sentMessages.Enqueue(message));

            var result = await client.CloseSessionAsync(new SessionCloseParams("session-123"));

            Assert.NotNull(result);
            Assert.True(sentMessages.TryDequeue(out var requestJson));

            using var document = JsonDocument.Parse(requestJson);
            Assert.Equal("session/close", document.RootElement.GetProperty("method").GetString());
            var @params = document.RootElement.GetProperty("params");
            Assert.Equal("session-123", @params.GetProperty("sessionId").GetString());
        }

        [Fact]
        public async Task CloseSessionAsync_WhenSupported_RemovesTrackedLocalSession()
        {
            var parser = new MessageParser();
            var sessionManager = new SessionManager();
            var client = new AcpClient(
                _transportMock.Object,
                parser,
                null,
                _errorLoggerMock.Object,
                sessionManager);

            SetupJsonRpcResponse(
                "initialize",
                JsonSerializer.SerializeToElement(
                    new InitializeResponse(
                        1,
                        new AgentInfo("TestAgent", "1.0.0"),
                        new AgentCapabilities(sessionCapabilities: new SessionCapabilities
                        {
                            Close = new SessionCloseCapabilities()
                        })),
                    parser.Options),
                parser);
            await client.InitializeAsync(new InitializeParams(
                new ClientInfo("Test", "1.0.0"),
                new ClientCapabilities()));

            SetupJsonRpcResponse(
                "session/new",
                JsonSerializer.SerializeToElement(new SessionNewResponse("session-123"), parser.Options),
                parser);
            await client.CreateSessionAsync(new SessionNewParams(AbsoluteCwd, null));
            Assert.NotNull(sessionManager.GetSession("session-123"));

            SetupJsonRpcResponse(
                "session/close",
                ElementFromJson("{}"),
                parser);
            await client.CloseSessionAsync(new SessionCloseParams("session-123"));

            Assert.Null(sessionManager.GetSession("session-123"));
        }

        [Theory]
        [InlineData(StopReason.MaxTurnRequests)]
        [InlineData(StopReason.Refusal)]
        [InlineData(StopReason.Cancelled)]
        public async Task SendPromptAsync_ParsesOfficialStopReasonValues(StopReason expected)
        {
            var parser = new MessageParser();
            var client = await CreateInitializedClientAsync();

            SetupJsonRpcResponse(
                "session/new",
                JsonSerializer.SerializeToElement(new SessionNewResponse("session-123"), parser.Options),
                parser);

            var createResult = await client.CreateSessionAsync(new SessionNewParams(AbsoluteCwd, null));

            SetupJsonRpcResponse(
                "session/prompt",
                JsonSerializer.SerializeToElement(new SessionPromptResponse(expected), parser.Options),
                parser);

            var promptResult = await client.SendPromptAsync(new SessionPromptParams(createResult.SessionId, new List<ContentBlock> { new TextContentBlock("hi") }));

            Assert.Equal(expected, promptResult.StopReason);
        }

        [Fact]
        public async Task SendPromptAsync_WhenSameSessionStreamingContinues_CompletesWhenResponseEventuallyArrives()
        {
            var parser = new MessageParser();
            var client = await CreateInitializedClientAsync();

            SetupJsonRpcResponse(
                "session/new",
                JsonSerializer.SerializeToElement(new SessionNewResponse("session-123"), parser.Options),
                parser);
            _transportMock.Setup(t => t.SendMessageAsync(It.IsRegex("session/prompt"), It.IsAny<CancellationToken>()))
                .Returns<string, CancellationToken>((message, cancellationToken) =>
                {
                    var request = parser.ParseRequest(message);

                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(20, cancellationToken);
                        var firstUpdate = new JsonRpcNotification(
                            "session/update",
                            JsonSerializer.SerializeToElement(
                                new SessionUpdateParams(
                                    "session-123",
                                    new AgentThoughtUpdate
                                    {
                                        Content = new TextContentBlock("thinking")
                                    }),
                                parser.Options));
                        RaiseTransportMessage(parser.SerializeMessage(firstUpdate));

                        await Task.Delay(20, cancellationToken);
                        var secondUpdate = new JsonRpcNotification(
                            "session/update",
                            JsonSerializer.SerializeToElement(
                                new SessionUpdateParams(
                                    "session-123",
                                    new AgentMessageUpdate(new TextContentBlock("still streaming"))),
                                parser.Options));
                        RaiseTransportMessage(parser.SerializeMessage(secondUpdate));

                        await Task.Delay(20, cancellationToken);
                        var thirdUpdate = new JsonRpcNotification(
                            "session/update",
                            JsonSerializer.SerializeToElement(
                                new SessionUpdateParams(
                                    "session-123",
                                    new AgentMessageUpdate(new TextContentBlock("more output"))),
                                parser.Options));
                        RaiseTransportMessage(parser.SerializeMessage(thirdUpdate));

                        await Task.Delay(20, cancellationToken);
                        var response = new JsonRpcResponse(
                            request.Id,
                            JsonSerializer.SerializeToElement(
                                new SessionPromptResponse(StopReason.EndTurn),
                                parser.Options));
                        RaiseTransportMessage(parser.SerializeMessage(response));
                    });

                    return Task.FromResult(true);
                });

            var createResult = await client.CreateSessionAsync(new SessionNewParams(AbsoluteCwd, null));

            var result = await client.SendPromptAsync(
                new SessionPromptParams(createResult.SessionId, new List<ContentBlock> { new TextContentBlock("hi") }));

            Assert.Equal(StopReason.EndTurn, result.StopReason);
        }

        [Fact]
        public async Task SendPromptAsync_WhenSessionIsTrackedByInjectedManager_AllowsWarmLoadedSession()
        {
            var parser = new MessageParser();
            var sessionManager = new SessionManager();
            await sessionManager.CreateSessionAsync("remote-1", AbsoluteCwd);

            var client = new AcpClient(
                _transportMock.Object,
                parser,
                null,
                _errorLoggerMock.Object,
                sessionManager);

            _transportMock
                .Setup(t => t.SendMessageAsync(It.IsRegex("initialize"), It.IsAny<CancellationToken>()))
                .Returns<string, CancellationToken>((_, _) =>
                {
                    var response = new JsonRpcResponse(
                        1,
                        JsonSerializer.SerializeToElement(
                            new InitializeResponse(
                                1,
                                new AgentInfo("TestAgent", "1.0.0"),
                                new AgentCapabilities(loadSession: true)),
                            parser.Options));
                    _transportMock.Raise(
                        t => t.MessageReceived += null,
                        new MessageReceivedEventArgs(parser.SerializeMessage(response)));
                    return Task.FromResult(true);
                });

            _transportMock
                .Setup(t => t.SendMessageAsync(It.IsRegex("session/prompt"), It.IsAny<CancellationToken>()))
                .Returns<string, CancellationToken>((_, _) =>
                {
                    var response = new JsonRpcResponse(
                        2,
                        JsonSerializer.SerializeToElement(new SessionPromptResponse(StopReason.EndTurn), parser.Options));
                    _transportMock.Raise(
                        t => t.MessageReceived += null,
                        new MessageReceivedEventArgs(parser.SerializeMessage(response)));
                    return Task.FromResult(true);
                });

            await client.InitializeAsync(new InitializeParams(new ClientInfo("Test", "1.0.0"), new ClientCapabilities()));

            var result = await client.SendPromptAsync(new SessionPromptParams("remote-1", new List<ContentBlock> { new TextContentBlock("hi") }));

            Assert.Equal(StopReason.EndTurn, result.StopReason);
        }

        [Fact]
        public async Task CancelSessionAsync_WhenPermissionPromptIsPending_SendsCancelledOutcomeImmediately()
        {
            var parser = new MessageParser();
            var client = await CreateInitializedClientAsync();
            var sentMessages = new ConcurrentQueue<string>();

            _transportMock
                .Setup(t => t.SendMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback<string, CancellationToken>((message, _) => sentMessages.Enqueue(message))
                .ReturnsAsync(true);

            client.PermissionRequestReceived += (_, _) =>
            {
                // Keep it pending to verify session/cancel actively drains it.
            };

            var request = new JsonRpcRequest(
                301,
                "session/request_permission",
                ElementFromJson(
                    """
                    {
                      "sessionId": "session-1",
                      "toolCall": {
                        "toolCallId": "call-1",
                        "title": "Read file",
                        "kind": "read",
                        "status": "pending"
                      },
                      "options": [
                        {
                          "optionId": "allow",
                          "name": "Allow",
                          "kind": "allow_once"
                        }
                      ]
                    }
                    """));

            _transportMock.Raise(
                t => t.MessageReceived += null,
                new MessageReceivedEventArgs(parser.SerializeMessage(request)));

            await client.CancelSessionAsync(new SessionCancelParams("session-1", "User cancelled"));

            var permissionResponse = await WaitForResponseAsync(parser, sentMessages, responseId: 301);
            Assert.False(permissionResponse.IsError);
            Assert.True(permissionResponse.Result.HasValue);

            var resultJson = permissionResponse.Result!.Value.GetRawText();
            using var resultDoc = JsonDocument.Parse(resultJson);
            Assert.Equal(
                "cancelled",
                resultDoc.RootElement.GetProperty("outcome").GetProperty("outcome").GetString());
        }

        [Fact]
        public async Task CancelSessionAsync_SendsSessionCancelAsNotificationWithoutRequestId()
        {
            var parser = new MessageParser();
            var client = await CreateInitializedClientAsync();
            string? sentPayload = null;

            _transportMock
                .Setup(t => t.SendMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback<string, CancellationToken>((message, _) => sentPayload = message)
                .ReturnsAsync(true);

            await client.CancelSessionAsync(new SessionCancelParams("session-42", "User cancelled"));

            Assert.NotNull(sentPayload);
            var parsed = parser.ParseMessage(sentPayload!);
            var notification = Assert.IsType<JsonRpcNotification>(parsed);
            Assert.Equal("session/cancel", notification.Method);
            Assert.True(notification.Params.HasValue);
            Assert.False(notification.Params.Value.TryGetProperty("id", out _));
            Assert.Equal(
                "session-42",
                notification.Params.Value.GetProperty("sessionId").GetString());
        }

        [Fact]
        public async Task SessionUpdateReceived_WhenMetaPrecedesDiscriminator_PublishesUpdate()
        {
            var client = await CreateInitializedClientAsync();
            SessionUpdateEventArgs? published = null;
            client.SessionUpdateReceived += (_, args) => published = args;

            const string notificationJson = """
            {
              "jsonrpc": "2.0",
              "method": "session/update",
              "params": {
                "sessionId": "sess-meta-runtime",
                "update": {
                  "_meta": {
                    "claudeCode": {
                      "toolName": "Bash"
                    }
                  },
                  "toolCallId": "call-runtime-1",
                  "sessionUpdate": "tool_call_update",
                  "status": "completed",
                  "title": "Run command"
                }
              }
            }
            """;

            _transportMock.Raise(
                t => t.MessageReceived += null,
                new MessageReceivedEventArgs(notificationJson));

            Assert.NotNull(published);
            Assert.Equal("sess-meta-runtime", published!.SessionId);
            var update = Assert.IsType<ToolCallStatusUpdate>(published.Update);
            Assert.Equal("call-runtime-1", update.ToolCallId);
            Assert.Equal(Domain.Models.Tool.ToolCallStatus.Completed, update.Status);
        }

        [Fact]
        public async Task TerminalRequests_WhenClientDidNotAdvertiseTerminal_ReturnMethodNotFound()
        {
            var parser = new MessageParser();
            var client = await CreateInitializedClientAsync();
            var sentMessages = new ConcurrentQueue<string>();

            _transportMock
                .Setup(t => t.SendMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback<string, CancellationToken>((message, _) => sentMessages.Enqueue(message))
                .ReturnsAsync(true);

            var createRequest = new JsonRpcRequest(
                99,
                "terminal/create",
                JsonSerializer.SerializeToElement(
                    new TerminalCreateRequest
                    {
                        SessionId = "session-1",
                        Command = "dotnet",
                        Args = new List<string> { "--version" },
                        OutputByteLimit = 4096
                    },
                    parser.Options));

            _transportMock.Raise(
                t => t.MessageReceived += null,
                new MessageReceivedEventArgs(parser.SerializeMessage(createRequest)));

            var createResponse = await WaitForResponseAsync(parser, sentMessages, responseId: 99);
            Assert.True(createResponse.IsError);
            Assert.Equal(JsonRpcErrorCode.MethodNotFound, createResponse.Error!.Code);
        }

        [Fact]
        public async Task TerminalRequests_WhenClientAdvertisedTerminal_ExecuteAndRespond()
        {
            var parser = new MessageParser();
            var terminalSessionManager = new Mock<ITerminalSessionManager>(MockBehavior.Strict);
            terminalSessionManager
                .Setup(x => x.CreateAsync(
                    It.Is<TerminalCreateRequest>(request =>
                        request.SessionId == "session-1"
                        && request.Command == "dotnet"),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new TerminalCreateResponse { TerminalId = "terminal-1" });
            var client = await CreateInitializedClientAsync(
                clientCapabilities: new ClientCapabilities(terminal: true),
                terminalSessionManager: terminalSessionManager.Object);
            var sentMessages = new ConcurrentQueue<string>();
            var terminalStates = new ConcurrentQueue<TerminalStateChangedEventArgs>();
            client.TerminalStateChangedReceived += (_, args) => terminalStates.Enqueue(args);

            _transportMock
                .Setup(t => t.SendMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback<string, CancellationToken>((message, _) => sentMessages.Enqueue(message))
                .ReturnsAsync(true);

            var createRequest = new JsonRpcRequest(
                99,
                "terminal/create",
                JsonSerializer.SerializeToElement(
                    new TerminalCreateRequest
                    {
                        SessionId = "session-1",
                        Command = "dotnet",
                        Args = new List<string> { "--version" },
                        OutputByteLimit = 4096
                    },
                    parser.Options));

            _transportMock.Raise(
                t => t.MessageReceived += null,
                new MessageReceivedEventArgs(parser.SerializeMessage(createRequest)));

            var createResponse = await WaitForResponseAsync(parser, sentMessages, responseId: 99);
            Assert.False(createResponse.IsError);
            var createResult = JsonSerializer.Deserialize<TerminalCreateResponse>(
                createResponse.Result!.Value.GetRawText(),
                parser.Options);
            Assert.NotNull(createResult);
            Assert.Equal("terminal-1", createResult!.TerminalId);
            Assert.Contains(
                terminalStates,
                state => state.SessionId == "session-1"
                    && state.TerminalId == createResult.TerminalId
                    && state.Method == "terminal/create");
            terminalSessionManager.VerifyAll();
        }

        [Fact]
        public async Task TerminalRequests_WhenTerminalAdvertisedWithoutInjectedManager_ReturnCapabilityNotSupported()
        {
            var parser = new MessageParser();
            var client = await CreateInitializedClientAsync(
                clientCapabilities: new ClientCapabilities(terminal: true));
            var sentMessages = new ConcurrentQueue<string>();

            _transportMock
                .Setup(t => t.SendMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback<string, CancellationToken>((message, _) => sentMessages.Enqueue(message))
                .ReturnsAsync(true);

            var createRequest = new JsonRpcRequest(
                99,
                "terminal/create",
                JsonSerializer.SerializeToElement(
                    new TerminalCreateRequest
                    {
                        SessionId = "session-1",
                        Command = "dotnet",
                        Args = new List<string> { "--version" },
                        OutputByteLimit = 4096
                    },
                    parser.Options));

            _transportMock.Raise(
                t => t.MessageReceived += null,
                new MessageReceivedEventArgs(parser.SerializeMessage(createRequest)));

            var createResponse = await WaitForResponseAsync(parser, sentMessages, responseId: 99);
            Assert.True(createResponse.IsError);
            Assert.Equal(JsonRpcErrorCode.CapabilityNotSupported, createResponse.Error!.Code);
            Assert.Contains("desktop process host", createResponse.Error.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task AskUserRequest_WhenAdvertised_PublishesEventAndReturnsStructuredResponse()
        {
            var parser = new MessageParser();
            var client = await CreateInitializedClientAsync(
                clientCapabilities: ClientCapabilityDefaults.Create());
            var sentMessages = new ConcurrentQueue<string>();
            AskUserRequestEventArgs? published = null;

            _transportMock
                .Setup(t => t.SendMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback<string, CancellationToken>((message, _) => sentMessages.Enqueue(message))
                .ReturnsAsync(true);

            client.AskUserRequestReceived += async (_, args) =>
            {
                published = args;
                await client.RespondToAskUserRequestAsync(
                    args.MessageId,
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["Choose a mode"] = "Plan"
                    });
            };

            var request = new JsonRpcRequest(
                201,
                ClientCapabilityMetadata.AskUserExtensionMethod,
                JsonSerializer.SerializeToElement(
                    new AskUserRequest
                    {
                        SessionId = "session-1",
                        Questions =
                        {
                            new AskUserQuestion
                            {
                                Header = "Execution",
                                Question = "Choose a mode",
                                MultiSelect = false,
                                Options =
                                {
                                    new AskUserOption { Label = "Agent", Description = "Interactive mode" },
                                    new AskUserOption { Label = "Plan", Description = "Planning mode" }
                                }
                            }
                        }
                    },
                    parser.Options));

            _transportMock.Raise(
                t => t.MessageReceived += null,
                new MessageReceivedEventArgs(parser.SerializeMessage(request)));

            var response = await WaitForResponseAsync(parser, sentMessages, responseId: 201);

            Assert.NotNull(published);
            Assert.Equal("session-1", published!.SessionId);
            Assert.False(response.IsError);

            var result = JsonSerializer.Deserialize<AskUserResponse>(
                response.Result!.Value.GetRawText(),
                parser.Options);

            Assert.NotNull(result);
            Assert.Single(result!.Questions);
            Assert.Equal("Plan", result.Answers["Choose a mode"]);
        }

        [Fact]
        public async Task AskUserRequest_WhenNotAdvertised_ReturnsMethodNotFound()
        {
            var parser = new MessageParser();
            var client = await CreateInitializedClientAsync();
            var sentMessages = new ConcurrentQueue<string>();

            _transportMock
                .Setup(t => t.SendMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback<string, CancellationToken>((message, _) => sentMessages.Enqueue(message))
                .ReturnsAsync(true);

            var request = new JsonRpcRequest(
                202,
                ClientCapabilityMetadata.AskUserExtensionMethod,
                JsonSerializer.SerializeToElement(
                    new AskUserRequest
                    {
                        SessionId = "session-1",
                        Questions =
                        {
                            new AskUserQuestion
                            {
                                Header = "Execution",
                                Question = "Choose a mode",
                                Options =
                                {
                                    new AskUserOption { Label = "Agent", Description = "Interactive mode" },
                                    new AskUserOption { Label = "Plan", Description = "Planning mode" }
                                }
                            }
                        }
                    },
                    parser.Options));

            _transportMock.Raise(
                t => t.MessageReceived += null,
                new MessageReceivedEventArgs(parser.SerializeMessage(request)));

            var response = await WaitForResponseAsync(parser, sentMessages, responseId: 202);

            Assert.True(response.IsError);
            Assert.Equal(JsonRpcErrorCode.MethodNotFound, response.Error!.Code);
        }

        [Fact]
        public async Task AskUserLegacyRequest_WhenOnlyModernExtensionAdvertised_ReturnsMethodNotFound()
        {
            var parser = new MessageParser();
            var client = await CreateInitializedClientAsync(
                clientCapabilities: ClientCapabilityDefaults.Create());
            var sentMessages = new ConcurrentQueue<string>();

            _transportMock
                .Setup(t => t.SendMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback<string, CancellationToken>((message, _) => sentMessages.Enqueue(message))
                .ReturnsAsync(true);

            var request = new JsonRpcRequest(
                203,
                "interaction.ask_user",
                JsonSerializer.SerializeToElement(
                    new AskUserRequest
                    {
                        SessionId = "session-1",
                        Questions =
                        {
                            new AskUserQuestion
                            {
                                Header = "Execution",
                                Question = "Choose a mode",
                                Options =
                                {
                                    new AskUserOption { Label = "Agent", Description = "Interactive mode" },
                                    new AskUserOption { Label = "Plan", Description = "Planning mode" }
                                }
                            }
                        }
                    },
                    parser.Options));

            _transportMock.Raise(
                t => t.MessageReceived += null,
                new MessageReceivedEventArgs(parser.SerializeMessage(request)));

            var response = await WaitForResponseAsync(parser, sentMessages, responseId: 203);

            Assert.True(response.IsError);
            Assert.Equal(JsonRpcErrorCode.MethodNotFound, response.Error!.Code);
        }

        [Fact]
        public async Task AskUserRequest_WhenPayloadInvalid_ReturnsInvalidParams()
        {
            var parser = new MessageParser();
            var client = await CreateInitializedClientAsync(
                clientCapabilities: ClientCapabilityDefaults.Create());
            var sentMessages = new ConcurrentQueue<string>();

            _transportMock
                .Setup(t => t.SendMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback<string, CancellationToken>((message, _) => sentMessages.Enqueue(message))
                .ReturnsAsync(true);

            var request = new JsonRpcRequest(
                204,
                ClientCapabilityMetadata.AskUserExtensionMethod,
                JsonSerializer.SerializeToElement(
                    new AskUserRequest
                    {
                        SessionId = "session-1",
                        Questions = new List<AskUserQuestion>()
                    },
                    parser.Options));

            _transportMock.Raise(
                t => t.MessageReceived += null,
                new MessageReceivedEventArgs(parser.SerializeMessage(request)));

            var response = await WaitForResponseAsync(parser, sentMessages, responseId: 204);

            Assert.True(response.IsError);
            Assert.Equal(JsonRpcErrorCode.InvalidParams, response.Error!.Code);
        }

        [Fact]
        public async Task RespondToPermissionRequestAsync_WhenSelectedWithoutOptionId_ThrowsInvalidParams()
        {
            var parser = new MessageParser();
            var client = await CreateInitializedClientAsync();
            PermissionRequestEventArgs? published = null;
            client.PermissionRequestReceived += (_, args) => published = args;

            var request = new JsonRpcRequest(
                205,
                "session/request_permission",
                ElementFromJson(
                    """
                    {
                      "sessionId": "session-1",
                      "toolCall": {
                        "toolCallId": "call-1",
                        "title": "Read file",
                        "kind": "read",
                        "status": "pending"
                      },
                      "options": [
                        {
                          "optionId": "allow",
                          "name": "Allow",
                          "kind": "allow_once"
                        }
                      ]
                    }
                    """));

            _transportMock.Raise(
                t => t.MessageReceived += null,
                new MessageReceivedEventArgs(parser.SerializeMessage(request)));
            await WaitForPublishedPermissionRequestAsync(() => published);

            var ex = await Assert.ThrowsAsync<AcpException>(() =>
                client.RespondToPermissionRequestAsync(205, "selected"));

            Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.ErrorCode);
        }

        [Fact]
        public async Task SessionRequestPermission_WhenPayloadIsStandard_PublishesAllOptions()
        {
            var parser = new MessageParser();
            var client = await CreateInitializedClientAsync();
            PermissionRequestEventArgs? published = null;
            client.PermissionRequestReceived += (_, args) => published = args;

            var request = new JsonRpcRequest(
                206,
                "session/request_permission",
                ElementFromJson(
                    """
                    {
                      "sessionId": "session-1",
                      "toolCall": {
                        "toolCallId": "call-1",
                        "title": "Run tests",
                        "kind": "execute",
                        "status": "pending"
                      },
                      "options": [
                        {
                          "optionId": "allow-once",
                          "name": "Allow once",
                          "kind": "allow_once",
                          "description": "Run this command once"
                        },
                        {
                          "optionId": "allow-always",
                          "name": "Always allow",
                          "kind": "allow_always"
                        },
                        {
                          "optionId": "reject-once",
                          "name": "Reject",
                          "kind": "reject_once"
                        }
                      ]
                    }
                    """));

            _transportMock.Raise(
                t => t.MessageReceived += null,
                new MessageReceivedEventArgs(parser.SerializeMessage(request)));
            await WaitForPublishedPermissionRequestAsync(() => published);

            Assert.NotNull(published);
            Assert.Equal("session-1", published!.SessionId);
            Assert.Equal(3, published.Options.Count);
            Assert.Equal("allow-once", published.Options[0].OptionId);
            Assert.Equal("Run this command once", published.Options[0].Description);
            Assert.Equal("allow-always", published.Options[1].OptionId);
            Assert.Equal("reject-once", published.Options[2].OptionId);
        }

        [Fact]
        public async Task SessionRequestPermission_WhenOptionsAreMissing_ReturnsInvalidParams()
        {
            var parser = new MessageParser();
            var client = await CreateInitializedClientAsync();
            var sentMessages = new ConcurrentQueue<string>();

            _transportMock
                .Setup(t => t.SendMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback<string, CancellationToken>((message, _) => sentMessages.Enqueue(message))
                .ReturnsAsync(true);

            var request = new JsonRpcRequest(
                207,
                "session/request_permission",
                ElementFromJson(
                    """
                    {
                      "sessionId": "session-1",
                      "toolCall": {
                        "toolCallId": "call-1"
                      }
                    }
                    """));

            _transportMock.Raise(
                t => t.MessageReceived += null,
                new MessageReceivedEventArgs(parser.SerializeMessage(request)));

            var response = await WaitForResponseAsync(parser, sentMessages, responseId: 207);

            Assert.True(response.IsError);
            Assert.Equal(JsonRpcErrorCode.InvalidParams, response.Error!.Code);
        }

        [Fact]
        public async Task SessionRequestPermission_WhenSessionIdIsNotString_ReturnsInvalidParams()
        {
            var parser = new MessageParser();
            var client = await CreateInitializedClientAsync();
            var sentMessages = new ConcurrentQueue<string>();

            _transportMock
                .Setup(t => t.SendMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback<string, CancellationToken>((message, _) => sentMessages.Enqueue(message))
                .ReturnsAsync(true);

            var request = new JsonRpcRequest(
                208,
                "session/request_permission",
                ElementFromJson(
                    """
                    {
                      "sessionId": 123,
                      "toolCall": {
                        "toolCallId": "call-1"
                      },
                      "options": [
                        {
                          "optionId": "allow-once",
                          "name": "Allow once",
                          "kind": "allow_once"
                        }
                      ]
                    }
                    """));

            _transportMock.Raise(
                t => t.MessageReceived += null,
                new MessageReceivedEventArgs(parser.SerializeMessage(request)));

            var response = await WaitForResponseAsync(parser, sentMessages, responseId: 208);

            Assert.True(response.IsError);
            Assert.Equal(JsonRpcErrorCode.InvalidParams, response.Error!.Code);
        }

        [Fact]
        public async Task SessionRequestPermission_WhenToolCallIsMissing_ReturnsInvalidParams()
        {
            var parser = new MessageParser();
            var client = await CreateInitializedClientAsync();
            var sentMessages = new ConcurrentQueue<string>();

            _transportMock
                .Setup(t => t.SendMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback<string, CancellationToken>((message, _) => sentMessages.Enqueue(message))
                .ReturnsAsync(true);

            var request = new JsonRpcRequest(
                209,
                "session/request_permission",
                ElementFromJson(
                    """
                    {
                      "sessionId": "session-1",
                      "options": [
                        {
                          "optionId": "allow-once",
                          "name": "Allow once",
                          "kind": "allow_once"
                        }
                      ]
                    }
                    """));

            _transportMock.Raise(
                t => t.MessageReceived += null,
                new MessageReceivedEventArgs(parser.SerializeMessage(request)));

            var response = await WaitForResponseAsync(parser, sentMessages, responseId: 209);

            Assert.True(response.IsError);
            Assert.Equal(JsonRpcErrorCode.InvalidParams, response.Error!.Code);
        }

        [Fact]
        public async Task SessionRequestPermission_WhenToolCallIdIsMissing_ReturnsInvalidParams()
        {
            var parser = new MessageParser();
            var client = await CreateInitializedClientAsync();
            var sentMessages = new ConcurrentQueue<string>();

            _transportMock
                .Setup(t => t.SendMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback<string, CancellationToken>((message, _) => sentMessages.Enqueue(message))
                .ReturnsAsync(true);

            var request = new JsonRpcRequest(
                210,
                "session/request_permission",
                ElementFromJson(
                    """
                    {
                      "sessionId": "session-1",
                      "toolCall": {},
                      "options": [
                        {
                          "optionId": "allow-once",
                          "name": "Allow once",
                          "kind": "allow_once"
                        }
                      ]
                    }
                    """));

            _transportMock.Raise(
                t => t.MessageReceived += null,
                new MessageReceivedEventArgs(parser.SerializeMessage(request)));

            var response = await WaitForResponseAsync(parser, sentMessages, responseId: 210);

            Assert.True(response.IsError);
            Assert.Equal(JsonRpcErrorCode.InvalidParams, response.Error!.Code);
        }

        [Fact]
        public async Task RespondToPermissionRequestAsync_WhenRequestIdIsUnknown_ReturnsFalseBeforePayloadValidation()
        {
            var client = await CreateInitializedClientAsync();

            var responded = await client.RespondToPermissionRequestAsync(999, "selected");

            Assert.False(responded);
        }

        [Fact]
        public async Task ListSessionsAsync_WhenAgentDoesNotSupportSessionList_DoesNotSendProtocolRequest()
        {
            var parser = new MessageParser();
            var client = await CreateInitializedClientAsync();

            SetupJsonRpcResponse(
                "session/list",
                JsonSerializer.SerializeToElement(new SessionListResponse(), parser.Options),
                parser);

            var result = await client.ListSessionsAsync(new SessionListParams());

            Assert.Empty(result.Sessions);
            _transportMock.Verify(
                t => t.SendMessageAsync(It.IsRegex("session/list"), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task ListSessionsAsync_WhenFilterCwdIsRelative_ThrowsInvalidParams()
        {
            var client = await CreateInitializedClientAsync(
                capabilities: new AgentCapabilities(sessionCapabilities: new SessionCapabilities
                {
                    List = new SessionListCapabilities()
                }));

            var ex = await Assert.ThrowsAsync<AcpException>(() =>
                client.ListSessionsAsync(new SessionListParams { Cwd = "relative-path" }));

            Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.ErrorCode);
            _transportMock.Verify(
                t => t.SendMessageAsync(It.IsRegex("session/list"), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task ListSessionsAsync_ParsesNextCursorFromRuntimeResponse()
        {
            var parser = new MessageParser();
            var client = await CreateInitializedClientAsync(
                capabilities: new AgentCapabilities(sessionCapabilities: new SessionCapabilities
                {
                    List = new SessionListCapabilities()
                }));

            SetupJsonRpcResponse(
                "session/list",
                ElementFromJson(
                    """
                    {
                      "sessions": [
                        {
                          "sessionId": "session-1",
                          "cwd": "/repo",
                          "title": "Imported"
                        }
                      ],
                      "nextCursor": "cursor-2"
                    }
                    """),
                parser);

            var result = await client.ListSessionsAsync(new SessionListParams());

            Assert.Equal("cursor-2", result.NextCursor);
        }

        [Fact]
        public async Task ListSessionsAsync_WhenResponseSessionCwdIsMissing_ThrowsParseError()
        {
            var parser = new MessageParser();
            var client = await CreateInitializedClientAsync(
                capabilities: new AgentCapabilities(sessionCapabilities: new SessionCapabilities
                {
                    List = new SessionListCapabilities()
                }));

            SetupJsonRpcResponse(
                "session/list",
                ElementFromJson(
                    """
                    {
                      "sessions": [
                        {
                          "sessionId": "session-1",
                          "title": "Imported"
                        }
                      ]
                    }
                    """),
                parser);

            var ex = await Assert.ThrowsAsync<AcpException>(() => client.ListSessionsAsync(new SessionListParams()));

            Assert.Equal(JsonRpcErrorCode.ParseError, ex.ErrorCode);
        }

        [Fact]
        public async Task ListSessionsAsync_WhenResponseSessionCwdIsRelative_ThrowsParseError()
        {
            var parser = new MessageParser();
            var client = await CreateInitializedClientAsync(
                capabilities: new AgentCapabilities(sessionCapabilities: new SessionCapabilities
                {
                    List = new SessionListCapabilities()
                }));

            SetupJsonRpcResponse(
                "session/list",
                ElementFromJson(
                    """
                    {
                      "sessions": [
                        {
                          "sessionId": "session-1",
                          "cwd": "relative-path",
                          "title": "Imported"
                        }
                      ]
                    }
                    """),
                parser);

            var ex = await Assert.ThrowsAsync<AcpException>(() => client.ListSessionsAsync(new SessionListParams()));

            Assert.Equal(JsonRpcErrorCode.ParseError, ex.ErrorCode);
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
                        && TryGetResponseId(response.Id, out var actualResponseId)
                        && actualResponseId == responseId)
                    {
                        return response;
                    }
                }

                await Task.Delay(20);
            }

            throw new TimeoutException($"Timed out waiting for JSON-RPC response {responseId}.");
        }

        private static JsonElement ElementFromJson(string json)
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }

        private void SetupJsonRpcResponse(
            string methodPattern,
            JsonElement? result,
            MessageParser parser,
            Action<string>? onSend = null,
            TimeSpan? responseDelay = null)
        {
            _transportMock
                .Setup(t => t.SendMessageAsync(It.IsRegex(methodPattern), It.IsAny<CancellationToken>()))
                .Returns<string, CancellationToken>((message, cancellationToken) =>
                {
                    onSend?.Invoke(message);
                    var request = parser.ParseRequest(message);
                    var response = new JsonRpcResponse(request.Id, result);
                    var serializedResponse = parser.SerializeMessage(response);

                    if (responseDelay is { } delay)
                    {
                        _ = Task.Run(
                            async () =>
                        {
                            await Task.Delay(delay).ConfigureAwait(false);
                            RaiseTransportMessage(serializedResponse);
                        });
                    }
                    else
                    {
                        RaiseTransportMessage(serializedResponse);
                    }

                    return Task.FromResult(true);
                });
        }

        private void RaiseTransportMessage(string message)
        {
            _transportMock.Raise(
                t => t.MessageReceived += null,
                new MessageReceivedEventArgs(message));
        }

        private static async Task WaitForPublishedPermissionRequestAsync(
            Func<PermissionRequestEventArgs?> getPublished,
            int timeoutMilliseconds = 5000)
        {
            var timeoutAt = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
            while (DateTime.UtcNow < timeoutAt)
            {
                if (getPublished() is not null)
                {
                    return;
                }

                await Task.Delay(20);
            }

            throw new TimeoutException("Timed out waiting for permission request publication.");
        }

        private static bool TryGetResponseId(object responseId, out long value)
        {
            switch (responseId)
            {
                case JsonElement { ValueKind: JsonValueKind.Number } jsonNumber when jsonNumber.TryGetInt64(out value):
                    return true;
                case byte byteValue:
                    value = byteValue;
                    return true;
                case short shortValue:
                    value = shortValue;
                    return true;
                case int intValue:
                    value = intValue;
                    return true;
                case long longValue:
                    value = longValue;
                    return true;
                default:
                    value = 0;
                    return false;
            }
        }
    }
}
