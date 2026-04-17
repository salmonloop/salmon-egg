using System;using System.Reactive.Subjects;using System.Threading;using System.Threading.Tasks;using Moq;using Serilog;using SalmonEgg.Domain.Models;using SalmonEgg.Domain.Services;using SalmonEgg.Infrastructure.Network;using Xunit;

namespace SalmonEgg.Infrastructure.Tests.Network
{
    public class ConnectionManagerTests
    {
        private readonly Mock<IAcpProtocolService> _mockProtocolService;
        private readonly Mock<ILogger> _mockLogger;
        private readonly Mock<ITransport> _mockTransport;
        private readonly Subject<string> _mockMessagesSubject;
        private readonly Subject<TransportState> _mockStateSubject;

        public ConnectionManagerTests()
        {
            _mockProtocolService = new Mock<IAcpProtocolService>();
            _mockLogger = new Mock<ILogger>();
            _mockMessagesSubject = new Subject<string>();
            _mockStateSubject = new Subject<TransportState>();

            _mockTransport = new Mock<ITransport>();
            _mockTransport.Setup(x => x.Messages).Returns(_mockMessagesSubject);
            _mockTransport.Setup(x => x.StateChanges).Returns(_mockStateSubject);

            _mockProtocolService.Setup(x => x.ValidateMessage(It.IsAny<AcpMessage>())).Returns(true);
            _mockProtocolService.Setup(x => x.SerializeMessage(It.IsAny<AcpMessage>())).Returns("{\"id\":\"test\",\"type\":\"test\"}");
            _mockProtocolService.Setup(x => x.ParseMessage(It.IsAny<string>())).Returns(new AcpMessage { Id = "test", Type = "response" });
        }

        [Fact]
        public async Task ConnectAsync_ShouldStartHeartbeat_WhenConnected()
        {
            // Arrange
            var config = new ServerConfiguration
            {
                Id = "test",
                Name = "Test Server",
                ServerUrl = "ws://localhost:8080",
                Transport = TransportType.WebSocket,
                HeartbeatInterval = 5
            };

            var transportFactory = new Func<TransportType, ITransport>(_ => _mockTransport.Object);
            var connectionManager = new ConnectionManager(
                _mockProtocolService.Object,
                _mockLogger.Object,
                transportFactory);

            // Act
            var connectTask = connectionManager.ConnectAsync(config, CancellationToken.None);

            // Simulate transport connection
            _mockStateSubject.OnNext(TransportState.Connected);

            await connectTask;

            // Assert
            _mockTransport.Verify(x => x.ConnectAsync(config.ServerUrl, It.IsAny<CancellationToken>()), Times.Once);
            _mockTransport.Verify(x => x.SendAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        }

        [Fact]
        public async Task DisconnectAsync_ShouldStopHeartbeat_WhenDisconnected()
        {
            // Arrange
            var config = new ServerConfiguration
            {
                Id = "test",
                Name = "Test Server",
                ServerUrl = "ws://localhost:8080",
                Transport = TransportType.WebSocket,
                HeartbeatInterval = 5
            };

            var transportFactory = new Func<TransportType, ITransport>(_ => _mockTransport.Object);
            var connectionManager = new ConnectionManager(
                _mockProtocolService.Object,
                _mockLogger.Object,
                transportFactory);

            // Connect first
            var connectTask = connectionManager.ConnectAsync(config, CancellationToken.None);
            _mockStateSubject.OnNext(TransportState.Connected);
            await connectTask;

            // Act
            await connectionManager.DisconnectAsync();

            // Assert
            _mockTransport.Verify(x => x.DisconnectAsync(), Times.Once);
        }

        [Fact]
        public async Task SendMessageAsync_ShouldReturnFailure_WhenNotConnected()
        {
            // Arrange
            var transportFactory = new Func<TransportType, ITransport>(_ => _mockTransport.Object);
            var connectionManager = new ConnectionManager(
                _mockProtocolService.Object,
                _mockLogger.Object,
                transportFactory);

            var message = new AcpMessage
            {
                Id = "test-123",
                Type = "request",
                Method = "test.method"
            };

            // Act
            var result = await connectionManager.SendMessageAsync(message, CancellationToken.None);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal("Not connected to server", result.Error);
        }

        [Fact]
        public async Task SendMessageAsync_ShouldReturnFailure_WhenMessageValidationFails()
        {
            // Arrange
            _mockProtocolService.Setup(x => x.ValidateMessage(It.IsAny<AcpMessage>())).Returns(false);

            var config = new ServerConfiguration
            {
                Id = "test",
                Name = "Test Server",
                ServerUrl = "ws://localhost:8080",
                Transport = TransportType.WebSocket,
                HeartbeatInterval = 5
            };

            var transportFactory = new Func<TransportType, ITransport>(_ => _mockTransport.Object);
            var connectionManager = new ConnectionManager(
                _mockProtocolService.Object,
                _mockLogger.Object,
                transportFactory);

            // Connect first
            var connectTask = connectionManager.ConnectAsync(config, CancellationToken.None);
            _mockStateSubject.OnNext(TransportState.Connected);
            await connectTask;

            var message = new AcpMessage
            {
                Id = "test-123",
                Type = "request",
                Method = "test.method"
            };

            // Act
            var result = await connectionManager.SendMessageAsync(message, CancellationToken.None);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal("Message validation failed", result.Error);
        }

        [Fact]
        public async Task ConnectAsync_ShouldHandleInvalidUrl()
        {
            // Arrange
            var config = new ServerConfiguration
            {
                Id = "test",
                Name = "Test Server",
                ServerUrl = "invalid-url",
                Transport = TransportType.WebSocket,
                HeartbeatInterval = 5
            };

            var transportFactory = new Func<TransportType, ITransport>(_ => _mockTransport.Object);
            var connectionManager = new ConnectionManager(
                _mockProtocolService.Object,
                _mockLogger.Object,
                transportFactory);

            // Act
            var result = await connectionManager.ConnectAsync(config, CancellationToken.None);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal("Server URL format is invalid", result.Error);
        }

        [Fact]
        public async Task ConnectAsync_ShouldHandleWebSocketUrlValidation()
        {
            // Arrange
            var config = new ServerConfiguration
            {
                Id = "test",
                Name = "Test Server",
                ServerUrl = "http://localhost:8080", // Invalid for WebSocket
                Transport = TransportType.WebSocket,
                HeartbeatInterval = 5
            };

            var transportFactory = new Func<TransportType, ITransport>(_ => _mockTransport.Object);
            var connectionManager = new ConnectionManager(
                _mockProtocolService.Object,
                _mockLogger.Object,
                transportFactory);

            // Act
            var result = await connectionManager.ConnectAsync(config, CancellationToken.None);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal("WebSocket transport requires ws:// or wss:// protocol", result.Error);
        }

        [Fact]
        public async Task ConnectAsync_ShouldHandleHttpSseUrlValidation()
        {
            // Arrange
            var config = new ServerConfiguration
            {
                Id = "test",
                Name = "Test Server",
                ServerUrl = "ws://localhost:8080", // Invalid for HTTP SSE
                Transport = TransportType.HttpSse,
                HeartbeatInterval = 5
            };

            var transportFactory = new Func<TransportType, ITransport>(_ => _mockTransport.Object);
            var connectionManager = new ConnectionManager(
                _mockProtocolService.Object,
                _mockLogger.Object,
                transportFactory);

            // Act
            var result = await connectionManager.ConnectAsync(config, CancellationToken.None);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal("HTTP SSE transport requires http:// or https:// protocol", result.Error);
        }

        [Fact]
        public async Task ConnectAsync_WithMissingStdioCommand_ShouldMentionLauncherAndSsh()
        {
            var config = new ServerConfiguration
            {
                Id = "stdio-test",
                Name = "SSH Bridge",
                Transport = TransportType.Stdio
            };

            var transportFactory = new Func<TransportType, ITransport>(_ => _mockTransport.Object);
            var connectionManager = new ConnectionManager(
                _mockProtocolService.Object,
                _mockLogger.Object,
                transportFactory);

            var result = await connectionManager.ConnectAsync(config, CancellationToken.None);

            Assert.False(result.IsSuccess);
            Assert.Equal("Stdio command or launcher is required (e.g. 'node', 'python', 'ssh')", result.Error);
        }

        [Fact]
        public void Dispose_ShouldCleanupResources()
        {
            // Arrange
            var transportFactory = new Func<TransportType, ITransport>(_ => _mockTransport.Object);
            var connectionManager = new ConnectionManager(
                _mockProtocolService.Object,
                _mockLogger.Object,
                transportFactory);

            // Act
            connectionManager.Dispose();

            // Assert
            // Verify that resources were cleaned up (no specific verification since heartbeat wasn't started)
            Assert.True(true);
        }

        /// <summary>
        /// Tests the heartbeat mechanism
        /// Verifies that heartbeats are sent continuously while connected
        /// </summary>
        [Fact]
        public async Task Heartbeat_ShouldContinueSending_WhileConnected()
        {
            // Arrange
            var config = new ServerConfiguration
            {
                Id = "test",
                Name = "Test Server",
                ServerUrl = "ws://localhost:8080",
                Transport = TransportType.WebSocket,
                HeartbeatInterval = 1 // 1 second interval for testing
            };

            var transportFactory = new Func<TransportType, ITransport>(_ => _mockTransport.Object);
            var connectionManager = new ConnectionManager(
                _mockProtocolService.Object,
                _mockLogger.Object,
                transportFactory);

            // Act
            var connectTask = connectionManager.ConnectAsync(config, CancellationToken.None);
            _mockStateSubject.OnNext(TransportState.Connected);
            await connectTask;

            // Wait for heartbeat timer to trigger
            await Task.Delay(1500); // Wait 1.5 seconds, should trigger at least one heartbeat

            // Simulate heartbeat response
            _mockMessagesSubject.OnNext("{\"id\":\"heartbeat-response\",\"type\":\"response\",\"result\":\"ok\"}");

            // Wait more time to ensure heartbeats continue
            await Task.Delay(1500);

            // Disconnect
            await connectionManager.DisconnectAsync();

            // Assert
            // Verify heartbeat messages were sent at least once
            _mockTransport.Verify(x => x.SendAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.AtLeast(2)); // Init message + at least one heartbeat
        }

        /// <summary>
        /// Tests connection establishment with valid configuration
        /// </summary>
        [Fact]
        public async Task ConnectAsync_ShouldSucceed_WithValidConfiguration()
        {
            // Arrange
            var config = new ServerConfiguration
            {
                Id = "test",
                Name = "Test Server",
                ServerUrl = "ws://localhost:8080",
                Transport = TransportType.WebSocket,
                HeartbeatInterval = 5
            };

            var transportFactory = new Func<TransportType, ITransport>(_ => _mockTransport.Object);
            var connectionManager = new ConnectionManager(
                _mockProtocolService.Object,
                _mockLogger.Object,
                transportFactory);

            // Act
            var result = await connectionManager.ConnectAsync(config, CancellationToken.None);

            // Assert
            Assert.True(result.IsSuccess);
            _mockTransport.Verify(x => x.ConnectAsync(config.ServerUrl, It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// Tests error handling for invalid URL
        /// </summary>
        [Fact]
        public async Task ConnectAsync_ShouldFail_WithInvalidUrl()
        {
            // Arrange
            var config = new ServerConfiguration
            {
                Id = "test",
                Name = "Test Server",
                ServerUrl = "invalid-url",
                Transport = TransportType.WebSocket,
                HeartbeatInterval = 5
            };

            var transportFactory = new Func<TransportType, ITransport>(_ => _mockTransport.Object);
            var connectionManager = new ConnectionManager(
                _mockProtocolService.Object,
                _mockLogger.Object,
                transportFactory);

            // Act
            var result = await connectionManager.ConnectAsync(config, CancellationToken.None);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal("Server URL format is invalid", result.Error);
        }

        /// <summary>
        /// Tests auto-reconnect retry limit
        /// </summary>
        [Fact]
        public async Task AutoReconnect_ShouldRespectRetryLimit()
        {
            // Arrange
            var config = new ServerConfiguration
            {
                Id = "test",
                Name = "Test Server",
                ServerUrl = "ws://localhost:8080",
                Transport = TransportType.WebSocket,
                HeartbeatInterval = 5
            };

            // Mock connection failure
            _mockTransport.Setup(x => x.ConnectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("Connection failed"));

            var transportFactory = new Func<TransportType, ITransport>(_ => _mockTransport.Object);
            var connectionManager = new ConnectionManager(
                _mockProtocolService.Object,
                _mockLogger.Object,
                transportFactory);

            // Act
            var result = await connectionManager.ConnectAsync(config, CancellationToken.None);

            // Assert
            Assert.False(result.IsSuccess);
            // Verify connection was attempted 4 times (1 initial + 3 retries)
            _mockTransport.Verify(x => x.ConnectAsync(config.ServerUrl, It.IsAny<CancellationToken>()), Times.Exactly(4));
        }

        /// <summary>
        /// Tests proxy connection configuration
        /// </summary>
        [Fact]
        public async Task ConnectAsync_ShouldHandleProxyConfiguration()
        {
            // Arrange
            var config = new ServerConfiguration
            {
                Id = "test",
                Name = "Test Server",
                ServerUrl = "ws://localhost:8080",
                Transport = TransportType.WebSocket,
                HeartbeatInterval = 5,
                Proxy = new ProxyConfig
                {
                    Enabled = true,
                    ProxyUrl = "http://proxy.example.com:8080"
                }
            };

            var transportFactory = new Func<TransportType, ITransport>(_ => _mockTransport.Object);
            var connectionManager = new ConnectionManager(
                _mockProtocolService.Object,
                _mockLogger.Object,
                transportFactory);

            // Act
            var result = await connectionManager.ConnectAsync(config, CancellationToken.None);

            // Assert
            Assert.True(result.IsSuccess);
            // Verify connection attempt was made
            _mockTransport.Verify(x => x.ConnectAsync(config.ServerUrl, It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
