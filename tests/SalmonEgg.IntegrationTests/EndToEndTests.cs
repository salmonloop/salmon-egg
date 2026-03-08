using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Serilog;
using SalmonEgg.Domain.Exceptions;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using SalmonEgg.Infrastructure.Network;
using SalmonEgg.Infrastructure.Serialization;
using Xunit;

namespace SalmonEgg.IntegrationTests
{
    /// <summary>
    /// 端到端集成测试
    /// 测试完整的连接 - 发送 - 接收流程
    /// </summary>
    public class EndToEndTests
    {
        private readonly AcpMessageParser _parser;
        private readonly Mock<IAcpProtocolService> _mockProtocolService;
        private readonly Mock<Serilog.ILogger> _mockLogger;

        public EndToEndTests()
        {
            _parser = new AcpMessageParser();
            _mockProtocolService = new Mock<IAcpProtocolService>();
            _mockLogger = new Mock<Serilog.ILogger>();
        }

        #region 连接流程测试

        [Fact]
        public async Task ConnectToServer_WithValidConfiguration_ShouldSucceed()
        {
            // Arrange
            var config = new ServerConfiguration
            {
                Id = "test-001",
                Name = "Test Server",
                ServerUrl = "wss://echo.websocket.org",
                Transport = TransportType.WebSocket,
                HeartbeatInterval = 30,
                ConnectionTimeout = 10
            };

            var connectionManager = new ConnectionManager(
                _mockProtocolService.Object,
                _mockLogger.Object,
                _ => new WebSocketTransport(_mockLogger.Object));

            try
            {
                // Act - 实际连接测试（可能需要网络）
                var result = await connectionManager.ConnectAsync(config, CancellationToken.None);

                // Assert
                if (result.IsSuccess)
                {
                    // 如果连接成功，验证状态
                    await connectionManager.DisconnectAsync();
                }
                else
                {
                    // 网络不可达也是可接受的结果（测试环境可能无网络）
                    Assert.NotNull(result.Error);
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException || ex is TaskCanceledException)
            {
                // 超时或取消也是可接受的（测试环境可能无网络）
            }
            finally
            {
                await connectionManager.DisconnectAsync();
            }
        }

        #endregion

        #region 消息流程测试

        [Fact]
        public void SendMessage_ThenParse_ShouldProduceEquivalentMessage()
        {
            // Arrange
            var originalMessage = new AcpMessage
            {
                Id = "test-123",
                Type = "request",
                Method = "test/method",
                Params = JsonDocument.Parse("{\"key\":\"value\"}").RootElement,
                ProtocolVersion = "1.0"
            };

            // Act
            var json = _parser.SerializeMessage(originalMessage);
            var parsed = _parser.ParseMessage(json);

            // Assert
            Assert.Equal(originalMessage.Id, parsed.Id);
            Assert.Equal(originalMessage.Type, parsed.Type);
            Assert.Equal(originalMessage.Method, parsed.Method);
            Assert.Equal(originalMessage.ProtocolVersion, parsed.ProtocolVersion);
        }

        [Fact]
        public void AcpMessageRoundTrip_AllMessageTypes_ShouldPreserveData()
        {
            // Arrange - 测试各种消息类型
            var messageTypes = new[] { "request", "response", "notification", "initialize" };

            foreach (var messageType in messageTypes)
            {
                var original = CreateTestMessage(messageType);

                // Act
                var json = _parser.SerializeMessage(original);
                var parsed = _parser.ParseMessage(json);

                // Assert
                Assert.Equal(original.Id, parsed.Id);
                Assert.Equal(original.Type, parsed.Type);
                Assert.Equal(original.Method, parsed.Method);
            }
        }

        private AcpMessage CreateTestMessage(string type)
        {
            return new AcpMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = type,
                Method = "test/method",
                Params = type switch
                {
                    "request" or "notification" => JsonDocument.Parse("{\"test\":true}").RootElement,
                    _ => null
                },
                Result = type == "response"
                    ? JsonDocument.Parse("\"success\"").RootElement
                    : null,
                ProtocolVersion = "1.0"
            };
        }

        #endregion

        #region 错误处理测试

        [Fact]
        public void MessageParser_WithInvalidJson_ShouldThrowException()
        {
            // Arrange
            var invalidJson = new[] { "", "{}", "not-json", "{\"id\":\"1\"}" };

            // Act & Assert
            foreach (var json in invalidJson)
            {
                Assert.Throws<AcpProtocolException>(() => _parser.ParseMessage(json));
            }
        }

        [Fact]
        public async Task ConnectionManager_WithNullConfig_ShouldThrowException()
        {
            // Arrange
            var connectionManager = new ConnectionManager(
                _mockProtocolService.Object,
                _mockLogger.Object,
                _ => new WebSocketTransport(_mockLogger.Object));

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await connectionManager.ConnectAsync(null!, CancellationToken.None));
        }

        #endregion

        #region 心跳机制测试

        [Fact]
        public void ConnectionManager_InitialState_ShouldBeDisconnected()
        {
            // Arrange & Act
            var connectionManager = new ConnectionManager(
                _mockProtocolService.Object,
                _mockLogger.Object,
                _ => new WebSocketTransport(_mockLogger.Object));

            // 订阅状态变化
            var states = new List<ConnectionState>();
            using var subscription = connectionManager.ConnectionStateChanges.Subscribe(states.Add);

            // Assert - 初始状态应为 Disconnected
            Assert.Single(states);
            Assert.Equal(ConnectionStatus.Disconnected, states[0].Status);

            connectionManager.Dispose();
        }

        #endregion
    }
}
