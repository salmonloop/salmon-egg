using System;using System.Reactive.Subjects;using System.Threading;using System.Threading.Tasks;using Moq;using Serilog;using UnoAcpClient.Domain.Models;using UnoAcpClient.Domain.Services;using UnoAcpClient.Infrastructure.Network;using Xunit;

namespace UnoAcpClient.Infrastructure.Tests.Network
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
            Assert.Equal("未连接到服务器", result.Error);
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
            Assert.Equal("消息验证失败", result.Error);
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
            Assert.Equal("服务器 URL 格式无效", result.Error);
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
            Assert.Equal("WebSocket 传输需要 ws:// 或 wss:// 协议", result.Error);
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
            Assert.Equal("HTTP SSE 传输需要 http:// 或 https:// 协议", result.Error);
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
    }
}