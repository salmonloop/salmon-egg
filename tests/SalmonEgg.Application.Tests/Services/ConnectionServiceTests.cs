using System;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using Moq;
using Serilog;
using SalmonEgg.Application.Common;
using SalmonEgg.Application.Services;
using SalmonEgg.Application.UseCases;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using Xunit;

namespace SalmonEgg.Application.Tests.Services
{
    public class ConnectionServiceTests
    {
        private readonly Mock<IConnectionManager> _mockConnectionManager;
        private readonly Mock<IConfigurationService> _mockConfigService;
        private readonly Mock<ILogger> _mockLogger;
        private readonly Mock<IValidator<ServerConfiguration>> _mockValidator;
        private readonly BehaviorSubject<ConnectionState> _connectionStateSubject;
        private readonly ConnectionService _service;
        private readonly ConnectToServerUseCase _connectUseCase;
        private readonly DisconnectUseCase _disconnectUseCase;

        public ConnectionServiceTests()
        {
            _mockConnectionManager = new Mock<IConnectionManager>();
            _mockConfigService = new Mock<IConfigurationService>();
            _mockLogger = new Mock<ILogger>();
            _mockValidator = new Mock<IValidator<ServerConfiguration>>();

            // 设置连接状态变化的可观察流
            _connectionStateSubject = new BehaviorSubject<ConnectionState>(new ConnectionState
            {
                Status = ConnectionStatus.Disconnected
            });

            _mockConnectionManager
                .Setup(x => x.ConnectionStateChanges)
                .Returns(_connectionStateSubject);

            // 创建真实的用例实例
            _connectUseCase = new ConnectToServerUseCase(
                _mockConnectionManager.Object,
                _mockConfigService.Object,
                _mockLogger.Object,
                _mockValidator.Object);

            _disconnectUseCase = new DisconnectUseCase(
                _mockConnectionManager.Object,
                _mockLogger.Object);

            _service = new ConnectionService(
                _connectUseCase,
                _disconnectUseCase,
                _mockConnectionManager.Object);
        }

        [Fact]
        public void Constructor_WithNullConnectUseCase_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                new ConnectionService(
                    null,
                    _disconnectUseCase,
                    _mockConnectionManager.Object));
        }

        [Fact]
        public void Constructor_WithNullDisconnectUseCase_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                new ConnectionService(
                    _connectUseCase,
                    null,
                    _mockConnectionManager.Object));
        }

        [Fact]
        public void Constructor_WithNullConnectionManager_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                new ConnectionService(
                    _connectUseCase,
                    _disconnectUseCase,
                    null));
        }

        [Fact]
        public void GetCurrentState_Initially_ShouldReturnDisconnectedState()
        {
            // Act
            var state = _service.GetCurrentState();

            // Assert
            Assert.NotNull(state);
            Assert.Equal(ConnectionStatus.Disconnected, state.Status);
        }

        [Fact]
        public async Task ConnectAsync_WithValidConfig_ShouldCallConnectionManager()
        {
            // Arrange
            var configId = "test-config-id";
            var config = new ServerConfiguration
            {
                Id = configId,
                Name = "Test Server",
                ServerUrl = "wss://test.example.com",
                Transport = TransportType.WebSocket
            };

            var connectedState = new ConnectionState
            {
                Status = ConnectionStatus.Connected,
                ServerUrl = config.ServerUrl,
                ConnectedAt = DateTime.UtcNow
            };

            _mockConfigService
                .Setup(x => x.LoadConfigurationAsync(configId))
                .ReturnsAsync(config);

            _mockValidator
                .Setup(x => x.ValidateAsync(config, default))
                .ReturnsAsync(new FluentValidation.Results.ValidationResult());

            _mockConnectionManager
                .Setup(x => x.ConnectAsync(config, It.IsAny<CancellationToken>()))
                .ReturnsAsync(ConnectionResult.Success(connectedState));

            // Act
            var result = await _service.ConnectAsync(configId);

            // Assert
            Assert.True(result.IsSuccess);
            _mockConnectionManager.Verify(
                x => x.ConnectAsync(config, It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task ConnectAsync_WithInvalidConfigId_ShouldReturnFailure()
        {
            // Arrange
            var configId = "invalid-config-id";
            _mockConfigService
                .Setup(x => x.LoadConfigurationAsync(configId))
                .ReturnsAsync((ServerConfiguration)null);

            // Act
            var result = await _service.ConnectAsync(configId);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Contains("未找到配置", result.Error);
        }

        [Fact]
        public async Task DisconnectAsync_ShouldCallConnectionManager()
        {
            // Arrange
            _mockConnectionManager
                .Setup(x => x.DisconnectAsync())
                .Returns(Task.CompletedTask);

            // Act
            await _service.DisconnectAsync();

            // Assert
            _mockConnectionManager.Verify(x => x.DisconnectAsync(), Times.Once);
        }

        [Fact]
        public void GetCurrentState_AfterConnectionStateChange_ShouldReturnUpdatedState()
        {
            // Arrange
            var newState = new ConnectionState
            {
                Status = ConnectionStatus.Connected,
                ServerUrl = "wss://test.example.com",
                ConnectedAt = DateTime.UtcNow
            };

            // Act
            _connectionStateSubject.OnNext(newState);
            var state = _service.GetCurrentState();

            // Assert
            Assert.NotNull(state);
            Assert.Equal(ConnectionStatus.Connected, state.Status);
            Assert.Equal("wss://test.example.com", state.ServerUrl);
            Assert.NotNull(state.ConnectedAt);
        }

        [Fact]
        public void GetCurrentState_AfterMultipleStateChanges_ShouldReturnLatestState()
        {
            // Arrange
            var state1 = new ConnectionState
            {
                Status = ConnectionStatus.Connecting,
                ServerUrl = "wss://test.example.com"
            };

            var state2 = new ConnectionState
            {
                Status = ConnectionStatus.Connected,
                ServerUrl = "wss://test.example.com",
                ConnectedAt = DateTime.UtcNow
            };

            var state3 = new ConnectionState
            {
                Status = ConnectionStatus.Disconnected
            };

            // Act
            _connectionStateSubject.OnNext(state1);
            _connectionStateSubject.OnNext(state2);
            _connectionStateSubject.OnNext(state3);
            var currentState = _service.GetCurrentState();

            // Assert
            Assert.NotNull(currentState);
            Assert.Equal(ConnectionStatus.Disconnected, currentState.Status);
        }

        [Fact]
        public void GetCurrentState_ShouldTrackReconnectingState()
        {
            // Arrange
            var reconnectingState = new ConnectionState
            {
                Status = ConnectionStatus.Reconnecting,
                ServerUrl = "wss://test.example.com",
                RetryCount = 2,
                ErrorMessage = "重试 2/3"
            };

            // Act
            _connectionStateSubject.OnNext(reconnectingState);
            var state = _service.GetCurrentState();

            // Assert
            Assert.NotNull(state);
            Assert.Equal(ConnectionStatus.Reconnecting, state.Status);
            Assert.Equal(2, state.RetryCount);
            Assert.Equal("重试 2/3", state.ErrorMessage);
        }

        [Fact]
        public void GetCurrentState_ShouldTrackErrorState()
        {
            // Arrange
            var errorState = new ConnectionState
            {
                Status = ConnectionStatus.Error,
                ServerUrl = "wss://test.example.com",
                ErrorMessage = "连接失败：网络不可达"
            };

            // Act
            _connectionStateSubject.OnNext(errorState);
            var state = _service.GetCurrentState();

            // Assert
            Assert.NotNull(state);
            Assert.Equal(ConnectionStatus.Error, state.Status);
            Assert.Equal("连接失败：网络不可达", state.ErrorMessage);
        }

        [Fact]
        public async Task ConnectAsync_WithEmptyConfigId_ShouldReturnFailure()
        {
            // Act
            var result = await _service.ConnectAsync(string.Empty);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Contains("配置 ID 不能为空", result.Error);
        }

        [Fact]
        public async Task ConnectAsync_WithNullConfigId_ShouldReturnFailure()
        {
            // Act
            var result = await _service.ConnectAsync(null);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Contains("配置 ID 不能为空", result.Error);
        }
    }
}
