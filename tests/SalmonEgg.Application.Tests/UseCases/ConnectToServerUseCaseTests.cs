using System;
using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using FluentValidation.Results;
using Moq;
using Serilog;
using SalmonEgg.Application.Services;
using SalmonEgg.Application.UseCases;
using SalmonEgg.Application.Validators;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using Xunit;

namespace SalmonEgg.Application.Tests.UseCases
{
    public class ConnectToServerUseCaseTests
    {
        private readonly Mock<IConnectionManager> _mockConnectionManager;
        private readonly Mock<IConfigurationService> _mockConfigService;
        private readonly Mock<ILogger> _mockLogger;
        private readonly IValidator<ServerConfiguration> _validator;
        private readonly ConnectToServerUseCase _useCase;

        public ConnectToServerUseCaseTests()
        {
            _mockConnectionManager = new Mock<IConnectionManager>();
            _mockConfigService = new Mock<IConfigurationService>();
            _mockLogger = new Mock<ILogger>();
            _validator = new ServerConfigurationValidator();

            _useCase = new ConnectToServerUseCase(
                _mockConnectionManager.Object,
                _mockConfigService.Object,
                _mockLogger.Object,
                _validator);
        }

        [Fact]
        public async Task ExecuteAsync_WithEmptyConfigId_ShouldReturnFailure()
        {
            // Act
            var result = await _useCase.ExecuteAsync("");

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Contains("配置 ID 不能为空", result.Error);
        }

        [Fact]
        public async Task ExecuteAsync_WithNonExistentConfigId_ShouldReturnFailure()
        {
            // Arrange
            _mockConfigService
                .Setup(x => x.LoadConfigurationAsync(It.IsAny<string>()))
                .ReturnsAsync((ServerConfiguration?)null);

            // Act
            var result = await _useCase.ExecuteAsync("non-existent-id");

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Contains("未找到配置 ID", result.Error);
        }

        [Fact]
        public async Task ExecuteAsync_WithInvalidConfiguration_ShouldReturnFailure()
        {
            // Arrange
            var invalidConfig = new ServerConfiguration
            {
                Id = "test-id",
                Name = "", // Invalid: empty name
                ServerUrl = "invalid-url", // Invalid: not a valid URL
                Transport = TransportType.WebSocket
            };

            _mockConfigService
                .Setup(x => x.LoadConfigurationAsync("test-id"))
                .ReturnsAsync(invalidConfig);

            // Act
            var result = await _useCase.ExecuteAsync("test-id");

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Contains("配置验证失败", result.Error);
        }

        [Fact]
        public async Task ExecuteAsync_WithValidConfigButConnectionFails_ShouldReturnFailure()
        {
            // Arrange
            var validConfig = new ServerConfiguration
            {
                Id = "test-id",
                Name = "Test Server",
                ServerUrl = "wss://test.example.com",
                Transport = TransportType.WebSocket,
                ConnectionTimeout = 10
            };

            _mockConfigService
                .Setup(x => x.LoadConfigurationAsync("test-id"))
                .ReturnsAsync(validConfig);

            _mockConnectionManager
                .Setup(x => x.ConnectAsync(It.IsAny<ServerConfiguration>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ConnectionResult.Failure("网络不可达"));

            // Act
            var result = await _useCase.ExecuteAsync("test-id");

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Contains("网络不可达", result.Error);
        }

        [Fact]
        public async Task ExecuteAsync_WithValidConfigAndSuccessfulConnection_ShouldReturnSuccess()
        {
            // Arrange
            var validConfig = new ServerConfiguration
            {
                Id = "test-id",
                Name = "Test Server",
                ServerUrl = "wss://test.example.com",
                Transport = TransportType.WebSocket,
                ConnectionTimeout = 10
            };

            var connectionState = new ConnectionState
            {
                Status = ConnectionStatus.Connected,
                ServerUrl = validConfig.ServerUrl,
                ConnectedAt = DateTime.UtcNow
            };

            _mockConfigService
                .Setup(x => x.LoadConfigurationAsync("test-id"))
                .ReturnsAsync(validConfig);

            _mockConnectionManager
                .Setup(x => x.ConnectAsync(It.IsAny<ServerConfiguration>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ConnectionResult.Success(connectionState));

            // Act
            var result = await _useCase.ExecuteAsync("test-id");

            // Assert
            Assert.True(result.IsSuccess);
            Assert.Null(result.Error);

            // Verify that connection was attempted
            _mockConnectionManager.Verify(
                x => x.ConnectAsync(validConfig, It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task ExecuteAsync_ShouldLogAppropriateMessages()
        {
            // Arrange
            var validConfig = new ServerConfiguration
            {
                Id = "test-id",
                Name = "Test Server",
                ServerUrl = "wss://test.example.com",
                Transport = TransportType.WebSocket,
                ConnectionTimeout = 10
            };

            var connectionState = new ConnectionState
            {
                Status = ConnectionStatus.Connected,
                ServerUrl = validConfig.ServerUrl,
                ConnectedAt = DateTime.UtcNow
            };

            _mockConfigService
                .Setup(x => x.LoadConfigurationAsync("test-id"))
                .ReturnsAsync(validConfig);

            _mockConnectionManager
                .Setup(x => x.ConnectAsync(It.IsAny<ServerConfiguration>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ConnectionResult.Success(connectionState));

            // Act
            var result = await _useCase.ExecuteAsync("test-id");

            // Assert
            Assert.True(result.IsSuccess);

            // Verify that configuration was loaded and connection was attempted
            _mockConfigService.Verify(
                x => x.LoadConfigurationAsync("test-id"),
                Times.Once);

            _mockConnectionManager.Verify(
                x => x.ConnectAsync(validConfig, It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task ExecuteAsync_WhenExceptionThrown_ShouldReturnFailureAndLogError()
        {
            // Arrange
            var exception = new Exception("Test exception");
            _mockConfigService
                .Setup(x => x.LoadConfigurationAsync("test-id"))
                .ThrowsAsync(exception);

            // Act
            var result = await _useCase.ExecuteAsync("test-id");

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Contains("连接失败：发生未预期的错误", result.Error);

            _mockLogger.Verify(
                x => x.Error(exception, "连接到服务器时发生未预期的错误，配置 ID: {ConfigId}", "test-id"),
                Times.Once);
        }
    }
}
