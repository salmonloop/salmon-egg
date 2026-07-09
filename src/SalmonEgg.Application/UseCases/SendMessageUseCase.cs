using System;
using System.Reactive.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using SalmonEgg.Application.Common;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Application.UseCases
{
    /// <summary>
    /// 发送消息用例
    /// 负责验证输入、检查连接状态、创建消息、发送消息并等待响应
    /// </summary>
    public class SendMessageUseCase
    {
        private readonly IConnectionManager _connectionManager;
        private readonly ILogger _logger;

        /// <summary>
        /// 初始化 SendMessageUseCase 的新实例
        /// </summary>
        /// <param name="connectionManager">连接管理器</param>
        /// <param name="logger">日志记录器</param>
        public SendMessageUseCase(
            IConnectionManager connectionManager,
            ILogger logger)
        {
            _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// 执行发送消息的操作
        /// </summary>
        /// <param name="method">方法名</param>
        /// <param name="parameters">已解析的 JSON 参数</param>
        /// <returns>包含响应消息的操作结果</returns>
        public async Task<Result<AcpMessage>> ExecuteAsync(string? method, JsonElement? parameters)
        {
            try
            {
                // 1. 验证输入 (Requirement 2.5, 4.3)
                if (string.IsNullOrWhiteSpace(method))
                {
                    _logger.Warning("发送消息失败：方法名为空");
                    return Result<AcpMessage>.Failure("方法名不能为空");
                }

                var methodValue = method!;
                _logger.Information("开始发送消息，方法: {Method}", methodValue);

                // 2. 检查连接状态 (Requirement 3.1)
                var currentState = await _connectionManager.ConnectionStateChanges
                    .FirstAsync();

                if (currentState.Status != ConnectionStatus.Connected)
                {
                    _logger.Warning("Failed to send message: Not connected to server, current status: {Status}", currentState.Status);
                    return Result<AcpMessage>.Failure($"Not connected to server, current status: {currentState.Status}");
                }

                _logger.Debug("连接状态检查通过，服务器: {ServerUrl}", currentState.ServerUrl);

                // 3. 创建消息 (Requirement 2.5)
                var message = new AcpMessage
                {
                    Id = Guid.NewGuid().ToString(),
                    Type = "request",
                    Method = methodValue,
                    Params = parameters.HasValue ? parameters.Value.Clone() : null,
                    ProtocolVersion = "1.0",
                    Timestamp = DateTime.UtcNow
                };

                _logger.Debug("创建消息，ID: {MessageId}, 方法: {Method}", message.Id, message.Method);

                // 4. 发送消息 (Requirement 4.3)
                var sendResult = await _connectionManager.SendMessageAsync(message, CancellationToken.None);

                if (!sendResult.IsSuccess)
                {
                    _logger.Error("发送消息失败: {Error}", sendResult.Error);
                    return Result<AcpMessage>.Failure($"发送消息失败: {sendResult.Error}");
                }

                _logger.Debug("消息已发送，等待响应...");

                var response = await _connectionManager.IncomingMessages
                    .Where(m => m.Id == message.Id && m.Type == "response")
                    .FirstAsync();

                // 检查响应是否包含错误
                if (response.Error != null)
                {
                    _logger.Warning(
                        "收到错误响应，消息 ID: {MessageId}, 错误代码: {ErrorCode}, 错误消息: {ErrorMessage}",
                        response.Id, response.Error.Code, response.Error.Message);

                    return Result<AcpMessage>.Failure(
                        $"服务器返回错误 (代码 {response.Error.Code}): {response.Error.Message}");
                }

                // 6. 记录日志 (Requirement 6.1)
                _logger.Information(
                    "成功接收响应，消息 ID: {MessageId}, 方法: {Method}",
                    response.Id, methodValue);

                return Result<AcpMessage>.Success(response);
            }
            catch (Exception ex)
            {
                // 记录详细的错误日志 (Requirement 6.1)
                _logger.Error(ex, "发送消息时发生未预期的错误，方法: {Method}", method);
                return Result<AcpMessage>.Failure("发送消息失败：发生未预期的错误");
            }
        }
    }
}
