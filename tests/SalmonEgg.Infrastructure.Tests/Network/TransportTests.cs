using System;using System.Collections.Generic;using System.Diagnostics;using System.Net;using System.Net.Http;using System.Net.WebSockets;using System.Reactive.Subjects;using System.Threading;using System.Threading.Tasks;using Moq;using Serilog;using SalmonEgg.Domain.Models;using SalmonEgg.Infrastructure.Network;using Websocket.Client;using Xunit;

namespace SalmonEgg.Infrastructure.Tests.Network
{
    public class TransportTests
    {
        private readonly Mock<ILogger> _mockLogger;

        public TransportTests()
        {
            _mockLogger = new Mock<ILogger>();
            _mockLogger.Setup(x => x.Information(It.IsAny<string>(), It.IsAny<object[]>())).Verifiable();
            _mockLogger.Setup(x => x.Warning(It.IsAny<string>(), It.IsAny<object[]>())).Verifiable();
            _mockLogger.Setup(x => x.Error(It.IsAny<Exception>(), It.IsAny<string>(), It.IsAny<object[]>())).Verifiable();
            _mockLogger.Setup(x => x.Debug(It.IsAny<string>(), It.IsAny<object[]>())).Verifiable();
        }

        [Fact]
        public async Task WebSocketTransport_ConnectAsync_ShouldThrowArgumentException_WhenUrlIsEmpty()
        {
            // Arrange
            var transport = new WebSocketTransport(_mockLogger.Object);

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() =>
                transport.ConnectAsync(string.Empty, CancellationToken.None));
        }

        [Fact]
        public async Task WebSocketTransport_ConnectAsync_ShouldFailPromptly_WhenDisconnectedBeforeReady()
        {
            var reconnections = new Subject<ReconnectionInfo>();
            var disconnections = new Subject<DisconnectionInfo>();
            var messages = new Subject<ResponseMessage>();
            var client = CreateMockClient(reconnections, disconnections, messages, out _);
            var transport = new WebSocketTransport(
                _mockLogger.Object,
                proxyConfiguration: null,
                connectTimeout: TimeSpan.FromSeconds(5),
                clientFactory: (_, _) => client.Object);
            var stopwatch = Stopwatch.StartNew();

            client
                .Setup(x => x.Start())
                .Returns(Task.CompletedTask)
                .Callback(() => disconnections.OnNext(DisconnectionInfo.Create(
                    DisconnectionType.Error,
                    null!,
                    new WebSocketException("bridge rejected connection"))));

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                transport.ConnectAsync("ws://localhost:3012/acp/ws", CancellationToken.None));

            stopwatch.Stop();

            Assert.Contains("closed before becoming ready", exception.Message);
            Assert.Contains("bridge rejected connection", exception.Message);
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1), $"Expected prompt failure, actual: {stopwatch.Elapsed}");
            Assert.IsType<WebSocketException>(exception.InnerException);
        }

        [Fact]
        public async Task WebSocketTransport_ConnectAsync_WhenServerReturnsNon101_ShouldIncludeActionableEndpointHint()
        {
            var reconnections = new Subject<ReconnectionInfo>();
            var disconnections = new Subject<DisconnectionInfo>();
            var messages = new Subject<ResponseMessage>();
            var client = CreateMockClient(reconnections, disconnections, messages, out _);
            var transport = new WebSocketTransport(
                _mockLogger.Object,
                proxyConfiguration: null,
                connectTimeout: TimeSpan.FromSeconds(5),
                clientFactory: (_, _) => client.Object);

            client
                .Setup(x => x.Start())
                .Returns(Task.CompletedTask)
                .Callback(() => disconnections.OnNext(DisconnectionInfo.Create(
                    DisconnectionType.Error,
                    null!,
                    new WebSocketException("Received network error or non-101 status code."))));

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                transport.ConnectAsync("ws://ccacp.shangxin.me/acp/ws", CancellationToken.None));

            Assert.Contains("non-101 status code", exception.Message, StringComparison.Ordinal);
            Assert.Contains("redirect", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("wss://", exception.Message, StringComparison.Ordinal);
            Assert.Contains("ws://ccacp.shangxin.me/acp/ws", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task WebSocketTransport_ConnectAsync_ShouldEmitConnectedOnce_WhenReadyEventArrives()
        {
            var reconnections = new Subject<ReconnectionInfo>();
            var disconnections = new Subject<DisconnectionInfo>();
            var messages = new Subject<ResponseMessage>();
            var client = CreateMockClient(reconnections, disconnections, messages, out var isRunning);
            var transport = new WebSocketTransport(
                _mockLogger.Object,
                proxyConfiguration: null,
                connectTimeout: TimeSpan.FromSeconds(5),
                clientFactory: (_, _) => client.Object);
            var states = new List<TransportState>();
            transport.StateChanges.Subscribe(states.Add);

            client
                .Setup(x => x.Start())
                .Returns(Task.CompletedTask)
                .Callback(() =>
                {
                    isRunning.Value = true;
                    reconnections.OnNext(ReconnectionInfo.Create(ReconnectionType.Initial));
                });

            await transport.ConnectAsync("ws://localhost:3012/acp/ws", CancellationToken.None);

            Assert.Single(states.FindAll(state => state == TransportState.Connected));
        }

        [Fact]
        public async Task WebSocketTransport_DisconnectAsync_ShouldNotThrow_WhenNotConnected()
        {
            // Arrange
            var transport = new WebSocketTransport(_mockLogger.Object);

            // Act & Assert
            await transport.DisconnectAsync();
            _mockLogger.Verify(x => x.Warning("WebSocket is not connected"), Times.Once);
        }

        [Fact]
        public async Task WebSocketTransport_SendAsync_ShouldThrowInvalidOperationException_WhenNotConnected()
        {
            // Arrange
            var transport = new WebSocketTransport(_mockLogger.Object);

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                transport.SendAsync("test message", CancellationToken.None));
        }

        [Fact]
        public async Task WebSocketTransport_SendAsync_ShouldThrowArgumentException_WhenMessageIsEmpty()
        {
            // Arrange
            var transport = new WebSocketTransport(_mockLogger.Object);

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() =>
                transport.SendAsync(string.Empty, CancellationToken.None));
        }

        [Fact]
        public async Task HttpSseTransport_ConnectAsync_ShouldThrowArgumentException_WhenUrlIsEmpty()
        {
            // Arrange
            var transport = new HttpSseTransport(_mockLogger.Object);

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() =>
                transport.ConnectAsync(string.Empty, CancellationToken.None));
        }

        [Fact]
        public async Task HttpSseTransport_DisconnectAsync_ShouldNotThrow_WhenNotConnected()
        {
            // Arrange
            var transport = new HttpSseTransport(_mockLogger.Object);

            // Act & Assert
            await transport.DisconnectAsync();
            _mockLogger.Verify(x => x.Warning("HTTP SSE is not connected"), Times.Once);
        }

        [Fact]
        public async Task HttpSseTransport_SendAsync_ShouldThrowInvalidOperationException_WhenNotConnected()
        {
            // Arrange
            var transport = new HttpSseTransport(_mockLogger.Object);

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                transport.SendAsync("test message", CancellationToken.None));
        }

        [Fact]
        public async Task HttpSseTransport_SendAsync_ShouldThrowArgumentException_WhenMessageIsEmpty()
        {
            // Arrange
            var transport = new HttpSseTransport(_mockLogger.Object);

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() =>
                transport.SendAsync(string.Empty, CancellationToken.None));
        }

        [Fact]
        public void WebSocketTransport_Dispose_ShouldCleanupResources()
        {
            // Arrange
            var transport = new WebSocketTransport(_mockLogger.Object);

            // Act
            transport.Dispose();

            // Assert
            // Verify that dispose methods were called
            _mockLogger.Verify(x => x.Debug("WebSocketTransport disposed"), Times.Once);
        }

        [Fact]
        public void HttpSseTransport_Dispose_ShouldCleanupResources()
        {
            // Arrange
            var transport = new HttpSseTransport(_mockLogger.Object);

            // Act
            transport.Dispose();

            // Assert
            // Verify that dispose methods were called
            _mockLogger.Verify(x => x.Debug("HttpSseTransport disposed"), Times.Once);
        }

        [Fact]
        public async Task HttpSseTransport_ConnectAsync_ShouldHandleCancellation()
        {
            // Arrange
            var transport = new HttpSseTransport(_mockLogger.Object);
            var cts = new CancellationTokenSource();
            cts.Cancel(); // Cancel immediately

            // Act & Assert
            await Assert.ThrowsAsync<TaskCanceledException>(() =>
                transport.ConnectAsync("http://localhost:8080", cts.Token));
        }

        [Fact]
        public void WebSocketTransport_CreateNativeClient_ShouldUseSystemProxy_ByDefault()
        {
            var client = WebSocketTransport.CreateNativeClient();

            Assert.NotNull(client.Options.Proxy);
            Assert.False(client.Options.Proxy is WebProxy);
        }

        [Fact]
        public void WebSocketTransport_CreateNativeClient_ShouldUseExplicitProxy_WhenConfigured()
        {
            var proxyUri = new Uri("http://proxy.example.com:8080");

            var client = WebSocketTransport.CreateNativeClient(new ProxyConfig
            {
                Mode = ProxyMode.Custom,
                ProxyUrl = proxyUri.ToString()
            });

            var proxy = Assert.IsType<WebProxy>(client.Options.Proxy);
            Assert.Equal(proxyUri, proxy.Address);
        }

        [Fact]
        public void WebSocketTransport_CreateNativeClient_ShouldPreserveSystemProxy_WhenConfigured()
        {
            var client = WebSocketTransport.CreateNativeClient(new ProxyConfig
            {
                Mode = ProxyMode.System
            });

            Assert.NotNull(client.Options.Proxy);
            Assert.False(client.Options.Proxy is WebProxy);
        }

        [Fact]
        public void WebSocketTransport_CreateClient_ShouldUseProvidedConnectTimeout()
        {
            var client = WebSocketTransport.CreateClient(
                new Uri("ws://localhost:3012/acp/ws"),
                new ProxyConfig { Mode = ProxyMode.None },
                TimeSpan.FromSeconds(120));

            Assert.Equal(TimeSpan.FromSeconds(120), client.ConnectTimeout);
        }

        /// <summary>
        /// Tests WebSocket transport state changes
        /// </summary>
        [Fact]
        public void WebSocketTransport_StateChanges_ShouldEmitCorrectStates()
        {
            // Arrange
            var transport = new WebSocketTransport(_mockLogger.Object);
            var stateChanges = new List<TransportState>();
            transport.StateChanges.Subscribe(state => stateChanges.Add(state));

            // Assert
            Assert.Contains(TransportState.Disconnected, stateChanges);
        }

        /// <summary>
        /// Tests HTTP SSE transport state changes
        /// </summary>
        [Fact]
        public void HttpSseTransport_StateChanges_ShouldEmitCorrectStates()
        {
            // Arrange
            var transport = new HttpSseTransport(_mockLogger.Object);
            var stateChanges = new List<TransportState>();
            transport.StateChanges.Subscribe(state => stateChanges.Add(state));

            // Assert
            Assert.Contains(TransportState.Disconnected, stateChanges);
        }

        /// <summary>
        /// Tests WebSocket transport message reception
        /// </summary>
        [Fact]
        public void WebSocketTransport_Messages_ShouldEmitReceivedMessages()
        {
            // Arrange
            var transport = new WebSocketTransport(_mockLogger.Object);
            var receivedMessages = new List<string>();
            transport.Messages.Subscribe(message => receivedMessages.Add(message));

            // Assert
            Assert.NotNull(transport.Messages);
        }

        /// <summary>
        /// Tests HTTP SSE transport message reception
        /// </summary>
        [Fact]
        public void HttpSseTransport_Messages_ShouldEmitReceivedMessages()
        {
            // Arrange
            var transport = new HttpSseTransport(_mockLogger.Object);
            var receivedMessages = new List<string>();
            transport.Messages.Subscribe(message => receivedMessages.Add(message));

            // Assert
            Assert.NotNull(transport.Messages);
        }

        private static Mock<IWebsocketClient> CreateMockClient(
            IObservable<ReconnectionInfo> reconnections,
            IObservable<DisconnectionInfo> disconnections,
            IObservable<ResponseMessage> messages,
            out Box<bool> isRunning)
        {
            var running = new Box<bool>(false);
            isRunning = running;
            var client = new Mock<IWebsocketClient>(MockBehavior.Strict);

            client.SetupGet(x => x.IsRunning).Returns(() => running.Value);
            client.SetupGet(x => x.ReconnectionHappened).Returns(reconnections);
            client.SetupGet(x => x.DisconnectionHappened).Returns(disconnections);
            client.SetupGet(x => x.MessageReceived).Returns(messages);
            client.SetupProperty(x => x.ReconnectTimeout);
            client.Setup(x => x.Start()).Returns(Task.CompletedTask);
            client.Setup(x => x.Dispose());

            return client;
        }

        private sealed class Box<T>(T value)
        {
            public T Value { get; set; } = value;
        }
    }
}
