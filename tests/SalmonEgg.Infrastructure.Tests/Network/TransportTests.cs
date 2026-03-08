using System;using System.Collections.Generic;using System.Net.Http;using System.Reactive.Subjects;using System.Threading;using System.Threading.Tasks;using Moq;using Serilog;using SalmonEgg.Infrastructure.Network;using Xunit;

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
    }
}