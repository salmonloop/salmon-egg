using System;
using System.Reactive.Subjects;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Serilog;
using SalmonEgg.Application.UseCases;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using Xunit;

namespace SalmonEgg.Application.Tests.UseCases
{
    public class SendMessageUseCaseTests
    {
        private readonly Mock<IConnectionManager> _mockConnectionManager;
        private readonly Mock<ILogger> _mockLogger;
        private readonly SendMessageUseCase _useCase;
        private readonly BehaviorSubject<ConnectionState> _connectionStateSubject;
        private readonly Subject<AcpMessage> _incomingMessagesSubject;

        public SendMessageUseCaseTests()
        {
            _mockConnectionManager = new Mock<IConnectionManager>();
            _mockLogger = new Mock<ILogger>();

            // 设置可观察流
            _connectionStateSubject = new BehaviorSubject<ConnectionState>(new ConnectionState
            {
                Status = ConnectionStatus.Connected,
                ServerUrl = "wss://test.example.com",
                ConnectedAt = DateTime.UtcNow
            });

            _incomingMessagesSubject = new Subject<AcpMessage>();

            _mockConnectionManager
                .Setup(x => x.ConnectionStateChanges)
                .Returns(_connectionStateSubject);

            _mockConnectionManager
                .Setup(x => x.IncomingMessages)
                .Returns(_incomingMessagesSubject);

            _useCase = new SendMessageUseCase(
                _mockConnectionManager.Object,
                _mockLogger.Object);
        }

        [Fact]
        public async Task ExecuteAsync_WithEmptyMethod_ShouldReturnFailure()
        {
            // Act
            var result = await _useCase.ExecuteAsync("", null);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Contains("方法名不能为空", result.Error);
        }

        [Fact]
        public async Task ExecuteAsync_WithNullMethod_ShouldReturnFailure()
        {
            // Act
            var result = await _useCase.ExecuteAsync(null, null);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Contains("方法名不能为空", result.Error);
        }

        [Fact]
        public async Task ExecuteAsync_WithInvalidTimeout_ShouldReturnFailure()
        {
            // Act
            var result = await _useCase.ExecuteAsync("test.method", null, -1);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Contains("超时时间必须大于 0", result.Error);
        }

        [Fact]
        public async Task ExecuteAsync_WhenNotConnected_ShouldReturnFailure()
        {
            // Arrange
            _connectionStateSubject.OnNext(new ConnectionState
            {
                Status = ConnectionStatus.Disconnected
            });

            // Act
            var result = await _useCase.ExecuteAsync("test.method", null);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Contains("Not connected to server", result.Error);
        }

        [Fact]
        public async Task ExecuteAsync_WhenSendFails_ShouldReturnFailure()
        {
            // Arrange
            _mockConnectionManager
                .Setup(x => x.SendMessageAsync(It.IsAny<AcpMessage>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(SendResult.Failure("网络错误"));

            // Act
            var result = await _useCase.ExecuteAsync("test.method", null);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Contains("发送消息失败", result.Error);
            Assert.Contains("网络错误", result.Error);
        }

        [Fact]
        public async Task ExecuteAsync_WhenResponseTimesOut_ShouldReturnFailure()
        {
            // Arrange
            _mockConnectionManager
                .Setup(x => x.SendMessageAsync(It.IsAny<AcpMessage>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(SendResult.Success("test-message-id"));

            // Act - 使用 1 秒超时，不发送响应
            var result = await _useCase.ExecuteAsync("test.method", null, 1);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Contains("请求超时", result.Error);
        }

        [Fact]
        public async Task ExecuteAsync_WhenResponseContainsError_ShouldReturnFailure()
        {
            // Arrange
            string messageId = null;
            
            _mockConnectionManager
                .Setup(x => x.SendMessageAsync(It.IsAny<AcpMessage>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((AcpMessage msg, CancellationToken ct) =>
                {
                    messageId = msg.Id;
                    return SendResult.Success(msg.Id);
                });

            // Act
            var resultTask = _useCase.ExecuteAsync("test.method", null, 5);

            // 等待一小段时间确保消息已发送
            await Task.Delay(100);

            // 发送错误响应
            _incomingMessagesSubject.OnNext(new AcpMessage
            {
                Id = messageId,
                Type = "response",
                Error = new AcpError
                {
                    Code = 500,
                    Message = "服务器内部错误"
                }
            });

            var result = await resultTask;

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Contains("服务器返回错误", result.Error);
            Assert.Contains("500", result.Error);
            Assert.Contains("服务器内部错误", result.Error);
        }

        [Fact]
        public async Task ExecuteAsync_WithValidRequestAndSuccessfulResponse_ShouldReturnSuccess()
        {
            // Arrange
            string messageId = null;
            var parameters = new { name = "test", value = 123 };
            
            _mockConnectionManager
                .Setup(x => x.SendMessageAsync(It.IsAny<AcpMessage>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((AcpMessage msg, CancellationToken ct) =>
                {
                    messageId = msg.Id;
                    return SendResult.Success(msg.Id);
                });

            // Act
            var resultTask = _useCase.ExecuteAsync("test.method", parameters, 5);

            // 等待一小段时间确保消息已发送
            await Task.Delay(100);

            // 发送成功响应
            var responseResult = JsonSerializer.SerializeToElement(new { status = "ok", data = "test data" });
            _incomingMessagesSubject.OnNext(new AcpMessage
            {
                Id = messageId,
                Type = "response",
                Result = responseResult
            });

            var result = await resultTask;

            // Assert
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.Equal(messageId, result.Value.Id);
            Assert.Equal("response", result.Value.Type);
            Assert.NotNull(result.Value.Result);
        }

        [Fact]
        public async Task ExecuteAsync_ShouldCreateMessageWithCorrectProperties()
        {
            // Arrange
            AcpMessage capturedMessage = null;
            var parameters = new { name = "test", value = 123 };
            
            _mockConnectionManager
                .Setup(x => x.SendMessageAsync(It.IsAny<AcpMessage>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((AcpMessage msg, CancellationToken ct) =>
                {
                    capturedMessage = msg;
                    
                    // 立即发送响应以避免超时
                    Task.Run(async () =>
                    {
                        await Task.Delay(50);
                        _incomingMessagesSubject.OnNext(new AcpMessage
                        {
                            Id = msg.Id,
                            Type = "response",
                            Result = JsonSerializer.SerializeToElement(new { status = "ok" })
                        });
                    });
                    
                    return SendResult.Success(msg.Id);
                });

            // Act
            var result = await _useCase.ExecuteAsync("test.method", parameters, 5);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.NotNull(capturedMessage);
            Assert.NotNull(capturedMessage.Id);
            Assert.NotEmpty(capturedMessage.Id);
            Assert.Equal("request", capturedMessage.Type);
            Assert.Equal("test.method", capturedMessage.Method);
            Assert.NotNull(capturedMessage.Params);
            Assert.Equal("1.0", capturedMessage.ProtocolVersion);
            Assert.True(capturedMessage.Timestamp > DateTime.MinValue);
        }

        [Fact]
        public async Task ExecuteAsync_WithNullParameters_ShouldCreateMessageWithNullParams()
        {
            // Arrange
            AcpMessage capturedMessage = null;
            
            _mockConnectionManager
                .Setup(x => x.SendMessageAsync(It.IsAny<AcpMessage>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((AcpMessage msg, CancellationToken ct) =>
                {
                    capturedMessage = msg;
                    
                    // 立即发送响应以避免超时
                    Task.Run(async () =>
                    {
                        await Task.Delay(50);
                        _incomingMessagesSubject.OnNext(new AcpMessage
                        {
                            Id = msg.Id,
                            Type = "response",
                            Result = JsonSerializer.SerializeToElement(new { status = "ok" })
                        });
                    });
                    
                    return SendResult.Success(msg.Id);
                });

            // Act
            var result = await _useCase.ExecuteAsync("test.method", null, 5);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.NotNull(capturedMessage);
            Assert.Null(capturedMessage.Params);
        }

        [Fact]
        public async Task ExecuteAsync_ShouldVerifyConnectionManagerCalls()
        {
            // Arrange
            string messageId = null;
            
            _mockConnectionManager
                .Setup(x => x.SendMessageAsync(It.IsAny<AcpMessage>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((AcpMessage msg, CancellationToken ct) =>
                {
                    messageId = msg.Id;
                    
                    // 立即发送响应
                    Task.Run(async () =>
                    {
                        await Task.Delay(50);
                        _incomingMessagesSubject.OnNext(new AcpMessage
                        {
                            Id = msg.Id,
                            Type = "response",
                            Result = JsonSerializer.SerializeToElement(new { status = "ok" })
                        });
                    });
                    
                    return SendResult.Success(msg.Id);
                });

            // Act
            var result = await _useCase.ExecuteAsync("test.method", null, 5);

            // Assert
            Assert.True(result.IsSuccess);

            // 验证调用了 SendMessageAsync
            _mockConnectionManager.Verify(
                x => x.SendMessageAsync(
                    It.Is<AcpMessage>(m => m.Method == "test.method" && m.Type == "request"),
                    It.IsAny<CancellationToken>()),
                Times.Once);

            // 验证访问了 ConnectionStateChanges
            _mockConnectionManager.Verify(
                x => x.ConnectionStateChanges,
                Times.AtLeastOnce);

            // 验证访问了 IncomingMessages
            _mockConnectionManager.Verify(
                x => x.IncomingMessages,
                Times.AtLeastOnce);
        }
    }
}
