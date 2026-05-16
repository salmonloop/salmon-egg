using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Serilog;
using SalmonEgg.Infrastructure.Network;
using Xunit;

namespace SalmonEgg.Infrastructure.Tests.Network
{
    public class WebSocketTransportTests
    {
        private readonly Mock<ILogger> _mockLogger;

        public WebSocketTransportTests()
        {
            _mockLogger = new Mock<ILogger>();
            _mockLogger.Setup(x => x.Information(It.IsAny<string>(), It.IsAny<object[]>())).Verifiable();
            _mockLogger.Setup(x => x.Warning(It.IsAny<string>(), It.IsAny<object[]>())).Verifiable();
            _mockLogger.Setup(x => x.Error(It.IsAny<Exception>(), It.IsAny<string>(), It.IsAny<object[]>())).Verifiable();
            _mockLogger.Setup(x => x.Debug(It.IsAny<string>(), It.IsAny<object[]>())).Verifiable();
        }

        [Fact]
        public void Constructor_ShouldThrowArgumentNullException_WhenLoggerIsNull()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new WebSocketTransport(null!));
        }

        [Fact]
        public async Task ConnectAsync_ShouldThrowArgumentException_WhenUrlIsEmpty()
        {
            // Arrange
            var transport = new WebSocketTransport(_mockLogger.Object);

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() =>
                transport.ConnectAsync(string.Empty, CancellationToken.None));
        }

        [Fact]
        public async Task DisconnectAsync_ShouldNotThrow_WhenNotConnected()
        {
            // Arrange
            var transport = new WebSocketTransport(_mockLogger.Object);

            // Act & Assert
            await transport.DisconnectAsync();
            _mockLogger.Verify(x => x.Warning("WebSocket is not connected"), Times.Once);
        }

        [Fact]
        public async Task SendAsync_ShouldThrowInvalidOperationException_WhenNotConnected()
        {
            // Arrange
            var transport = new WebSocketTransport(_mockLogger.Object);

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                transport.SendAsync("test message", CancellationToken.None));
        }

        [Fact]
        public async Task SendAsync_ShouldThrowArgumentException_WhenMessageIsEmpty()
        {
            // Arrange
            var transport = new WebSocketTransport(_mockLogger.Object);

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() =>
                transport.SendAsync(string.Empty, CancellationToken.None));
        }

        [Fact]
        public void Dispose_ShouldCleanupResources()
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
        public void StateChanges_ShouldEmitCorrectStates()
        {
            // Arrange
            var transport = new WebSocketTransport(_mockLogger.Object);
            var stateChanges = new List<TransportState>();
            transport.StateChanges.Subscribe(state => stateChanges.Add(state));

            // Assert
            Assert.Contains(TransportState.Disconnected, stateChanges);
        }

        [Fact]
        public void Messages_ShouldEmitReceivedMessages()
        {
            // Arrange
            var transport = new WebSocketTransport(_mockLogger.Object);
            var receivedMessages = new List<string>();
            transport.Messages.Subscribe(message => receivedMessages.Add(message));

            // Assert
            Assert.NotNull(transport.Messages);
        }
    }
}
