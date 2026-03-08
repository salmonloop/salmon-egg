using System;
using System.Threading.Tasks;
using Moq;
using Serilog;
using SalmonEgg.Application.UseCases;
using SalmonEgg.Domain.Services;
using Xunit;

namespace SalmonEgg.Application.Tests.UseCases
{
    public class DisconnectUseCaseTests
    {
        private readonly Mock<IConnectionManager> _mockConnectionManager;
        private readonly Mock<ILogger> _mockLogger;
        private readonly DisconnectUseCase _useCase;

        public DisconnectUseCaseTests()
        {
            _mockConnectionManager = new Mock<IConnectionManager>();
            _mockLogger = new Mock<ILogger>();

            _useCase = new DisconnectUseCase(
                _mockConnectionManager.Object,
                _mockLogger.Object);
        }

        [Fact]
        public async Task ExecuteAsync_ShouldCallDisconnectAsync()
        {
            // Arrange
            _mockConnectionManager
                .Setup(x => x.DisconnectAsync())
                .Returns(Task.CompletedTask);

            // Act
            var result = await _useCase.ExecuteAsync();

            // Assert
            Assert.True(result.IsSuccess);
            _mockConnectionManager.Verify(x => x.DisconnectAsync(), Times.Once);
        }

        [Fact]
        public async Task ExecuteAsync_WhenDisconnectSucceeds_ShouldReturnSuccess()
        {
            // Arrange
            _mockConnectionManager
                .Setup(x => x.DisconnectAsync())
                .Returns(Task.CompletedTask);

            // Act
            var result = await _useCase.ExecuteAsync();

            // Assert
            Assert.True(result.IsSuccess);
            Assert.Null(result.Error);
        }

        [Fact]
        public async Task ExecuteAsync_WhenDisconnectThrowsException_ShouldReturnFailure()
        {
            // Arrange
            _mockConnectionManager
                .Setup(x => x.DisconnectAsync())
                .ThrowsAsync(new InvalidOperationException("Connection error"));

            // Act
            var result = await _useCase.ExecuteAsync();

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Contains("断开连接失败", result.Error);
        }

        [Fact]
        public void Constructor_WithNullConnectionManager_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                new DisconnectUseCase(null, _mockLogger.Object));
        }

        [Fact]
        public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                new DisconnectUseCase(_mockConnectionManager.Object, null));
        }
    }
}
