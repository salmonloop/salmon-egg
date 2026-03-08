using System;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
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
    public class MessageServiceTests
    {
        private readonly Mock<IConnectionManager> _mockConnectionManager;
        private readonly Mock<ILogger> _mockLogger;
        private readonly Subject<AcpMessage> _incomingMessagesSubject;
        private readonly BehaviorSubject<ConnectionState> _connectionStateSubject;
        private readonly SendMessageUseCase _sendMessageUseCase;
        private readonly MessageService _service;

        public MessageServiceTests()
        {
            _mockConnectionManager = new Mock<IConnectionManager>();
            _mockLogger = new Mock<ILogger>();

            // 设置传入消息的可观察流
            _incomingMessagesSubject = new Subject<AcpMessage>();
            _mockConnectionManager
                .Setup(x => x.IncomingMessages)
                .Returns(_incomingMessagesSubject);

            // 设置连接状态变化的可观察流
            _connectionStateSubject = new BehaviorSubject<ConnectionState>(new ConnectionState
            {
                Status = ConnectionStatus.Connected,
                ServerUrl = "wss://test.example.com"
            });
            _mockConnectionManager
                .Setup(x => x.ConnectionStateChanges)
                .Returns(_connectionStateSubject);

            // 创建真实的 SendMessageUseCase 实例
            _sendMessageUseCase = new SendMessageUseCase(
                _mockConnectionManager.Object,
                _mockLogger.Object);

            _service = new MessageService(
                _sendMessageUseCase,
                _mockConnectionManager.Object,
                _mockLogger.Object);
        }

        [Fact]
        public void Constructor_WithNullSendMessageUseCase_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                new MessageService(
                    null,
                    _mockConnectionManager.Object,
                    _mockLogger.Object));
        }

        [Fact]
        public void Constructor_WithNullConnectionManager_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                new MessageService(
                    _sendMessageUseCase,
                    null,
                    _mockLogger.Object));
        }

        [Fact]
        public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                new MessageService(
                    _sendMessageUseCase,
                    _mockConnectionManager.Object,
                    null));
        }

        [Fact]
        public void Notifications_ShouldNotBeNull()
        {
            // Assert
            Assert.NotNull(_service.Notifications);
        }

        [Fact]
        public async Task SendRequestAsync_WithValidMethodAndParameters_ShouldReturnSuccess()
        {
            // Arrange
            var method = "test.method";
            var parameters = new { value = "test" };
            var messageId = string.Empty;

            _mockConnectionManager
                .Setup(x => x.SendMessageAsync(It.IsAny<AcpMessage>(), It.IsAny<CancellationToken>()))
                .Callback<AcpMessage, CancellationToken>((msg, ct) => messageId = msg.Id)
                .ReturnsAsync(() => SendResult.Success(messageId));

            // 模拟响应消息
            var responseTask = _service.SendRequestAsync(method, parameters);
            
            // 等待一小段时间确保消息已发送
            await Task.Delay(100);
            
            // 发送响应
            _incomingMessagesSubject.OnNext(new AcpMessage
            {
                Id = messageId,
                Type = "response",
                Result = System.Text.Json.JsonSerializer.SerializeToElement(new { success = true })
            });

            var result = await responseTask;

            // Assert
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.Equal("response", result.Value.Type);
        }

        [Fact]
        public async Task SendRequestAsync_WithNullMethod_ShouldReturnFailure()
        {
            // Act
            var result = await _service.SendRequestAsync(null, null);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Contains("方法名不能为空", result.Error);
        }

        [Fact]
        public async Task SendRequestAsync_WithEmptyMethod_ShouldReturnFailure()
        {
            // Act
            var result = await _service.SendRequestAsync(string.Empty, null);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Contains("方法名不能为空", result.Error);
        }

        [Fact]
        public async Task SendRequestAsync_WithWhitespaceMethod_ShouldReturnFailure()
        {
            // Act
            var result = await _service.SendRequestAsync("   ", null);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Contains("方法名不能为空", result.Error);
        }

        [Fact]
        public async Task SendRequestAsync_WhenNotConnected_ShouldReturnFailure()
        {
            // Arrange
            _connectionStateSubject.OnNext(new ConnectionState
            {
                Status = ConnectionStatus.Disconnected
            });

            // Act
            var result = await _service.SendRequestAsync("test.method", null);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Contains("Not connected to server", result.Error);
        }

        [Fact]
        public async Task SendRequestAsync_WhenSendFails_ShouldReturnFailure()
        {
            // Arrange
            var method = "test.method";
            _mockConnectionManager
                .Setup(x => x.SendMessageAsync(It.IsAny<AcpMessage>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(SendResult.Failure("网络错误"));

            // Act
            var result = await _service.SendRequestAsync(method, null);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Contains("发送消息失败", result.Error);
        }

        [Fact]
        public async Task Notifications_ShouldFilterNotificationMessages()
        {
            // Arrange
            AcpMessage receivedNotification = null;
            _service.Notifications.Subscribe(msg => receivedNotification = msg);

            var notification = new AcpMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = "notification",
                Method = "server.notification",
                Timestamp = DateTime.UtcNow
            };

            // Act
            _incomingMessagesSubject.OnNext(notification);
            await Task.Delay(100); // 等待订阅处理

            // Assert
            Assert.NotNull(receivedNotification);
            Assert.Equal("notification", receivedNotification.Type);
            Assert.Equal("server.notification", receivedNotification.Method);
        }

        [Fact]
        public async Task Notifications_ShouldNotIncludeResponseMessages()
        {
            // Arrange
            var notificationCount = 0;
            _service.Notifications.Subscribe(_ => notificationCount++);

            var response = new AcpMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = "response",
                Result = System.Text.Json.JsonSerializer.SerializeToElement(new { success = true })
            };

            // Act
            _incomingMessagesSubject.OnNext(response);
            await Task.Delay(100); // 等待订阅处理

            // Assert
            Assert.Equal(0, notificationCount);
        }

        [Fact]
        public async Task Notifications_ShouldNotIncludeRequestMessages()
        {
            // Arrange
            var notificationCount = 0;
            _service.Notifications.Subscribe(_ => notificationCount++);

            var request = new AcpMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = "request",
                Method = "test.method"
            };

            // Act
            _incomingMessagesSubject.OnNext(request);
            await Task.Delay(100); // 等待订阅处理

            // Assert
            Assert.Equal(0, notificationCount);
        }

        [Fact]
        public async Task Notifications_ShouldReceiveMultipleNotifications()
        {
            // Arrange
            var receivedNotifications = new System.Collections.Generic.List<AcpMessage>();
            _service.Notifications.Subscribe(msg => receivedNotifications.Add(msg));

            var notification1 = new AcpMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = "notification",
                Method = "notification.one"
            };

            var notification2 = new AcpMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = "notification",
                Method = "notification.two"
            };

            var notification3 = new AcpMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = "notification",
                Method = "notification.three"
            };

            // Act
            _incomingMessagesSubject.OnNext(notification1);
            _incomingMessagesSubject.OnNext(notification2);
            _incomingMessagesSubject.OnNext(notification3);
            await Task.Delay(100); // 等待订阅处理

            // Assert
            Assert.Equal(3, receivedNotifications.Count);
            Assert.Equal("notification.one", receivedNotifications[0].Method);
            Assert.Equal("notification.two", receivedNotifications[1].Method);
            Assert.Equal("notification.three", receivedNotifications[2].Method);
        }

        [Fact]
        public async Task SendRequestAsync_WithComplexParameters_ShouldSucceed()
        {
            // Arrange
            var method = "complex.method";
            var parameters = new
            {
                name = "test",
                value = 42,
                nested = new
                {
                    flag = true,
                    items = new[] { 1, 2, 3 }
                }
            };
            var messageId = string.Empty;

            _mockConnectionManager
                .Setup(x => x.SendMessageAsync(It.IsAny<AcpMessage>(), It.IsAny<CancellationToken>()))
                .Callback<AcpMessage, CancellationToken>((msg, ct) => messageId = msg.Id)
                .ReturnsAsync(() => SendResult.Success(messageId));

            var responseTask = _service.SendRequestAsync(method, parameters);
            await Task.Delay(100);

            _incomingMessagesSubject.OnNext(new AcpMessage
            {
                Id = messageId,
                Type = "response",
                Result = System.Text.Json.JsonSerializer.SerializeToElement(new { processed = true })
            });

            var result = await responseTask;

            // Assert
            Assert.True(result.IsSuccess);
            _mockConnectionManager.Verify(
                x => x.SendMessageAsync(
                    It.Is<AcpMessage>(m => m.Method == method && m.Params != null),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task SendRequestAsync_WithErrorResponse_ShouldReturnFailure()
        {
            // Arrange
            var method = "test.method";
            var messageId = string.Empty;

            _mockConnectionManager
                .Setup(x => x.SendMessageAsync(It.IsAny<AcpMessage>(), It.IsAny<CancellationToken>()))
                .Callback<AcpMessage, CancellationToken>((msg, ct) => messageId = msg.Id)
                .ReturnsAsync(() => SendResult.Success(messageId));

            var responseTask = _service.SendRequestAsync(method, null);
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

            var result = await responseTask;

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Contains("服务器返回错误", result.Error);
            Assert.Contains("500", result.Error);
        }

        [Fact]
        public async Task Notifications_ShouldHandleNotificationsWithParams()
        {
            // Arrange
            AcpMessage receivedNotification = null;
            _service.Notifications.Subscribe(msg => receivedNotification = msg);

            var notification = new AcpMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = "notification",
                Method = "data.updated",
                Params = System.Text.Json.JsonSerializer.SerializeToElement(new { id = 123, status = "active" })
            };

            // Act
            _incomingMessagesSubject.OnNext(notification);
            await Task.Delay(100);

            // Assert
            Assert.NotNull(receivedNotification);
            Assert.Equal("notification", receivedNotification.Type);
            Assert.Equal("data.updated", receivedNotification.Method);
            Assert.NotNull(receivedNotification.Params);
        }
    }
}
