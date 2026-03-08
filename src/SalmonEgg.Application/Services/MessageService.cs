using System;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Serilog;
using SalmonEgg.Application.Common;
using SalmonEgg.Application.UseCases;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Application.Services
{
    /// <summary>
    /// 消息服务实现
    /// 封装 SendMessageUseCase 并提供通知消息的可观察流
    /// Requirements: 2.5, 4.4
    /// </summary>
    public class MessageService : IMessageService
    {
        private readonly SendMessageUseCase _sendMessageUseCase;
        private readonly IConnectionManager _connectionManager;
        private readonly ILogger _logger;

        /// <summary>
        /// 初始化 MessageService 的新实例
        /// </summary>
        /// <param name="sendMessageUseCase">发送消息用例</param>
        /// <param name="connectionManager">连接管理器</param>
        /// <param name="logger">日志记录器</param>
        public MessageService(
            SendMessageUseCase sendMessageUseCase,
            IConnectionManager connectionManager,
            ILogger logger)
        {
            _sendMessageUseCase = sendMessageUseCase ?? throw new ArgumentNullException(nameof(sendMessageUseCase));
            _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // 初始化通知消息流 (Requirement 4.4)
            // 过滤出类型为 "notification" 的消息
            Notifications = _connectionManager.IncomingMessages
                .Where(m => m.Type == "notification")
                .Do(notification => _logger.Information(
                    "收到通知消息，方法: {Method}, ID: {MessageId}",
                    notification.Method, notification.Id));
        }

        /// <summary>
        /// 异步发送请求消息
        /// </summary>
        /// <param name="method">方法名</param>
        /// <param name="parameters">请求参数</param>
        /// <returns>包含响应消息的操作结果</returns>
        public async Task<Result<AcpMessage>> SendRequestAsync(string method, object parameters)
        {
            _logger.Debug("MessageService: 调用 SendMessageUseCase，方法: {Method}", method);
            
            // 委托给 SendMessageUseCase (Requirement 2.5)
            var result = await _sendMessageUseCase.ExecuteAsync(method, parameters);
            
            if (result.IsSuccess)
            {
                _logger.Debug("MessageService: 消息发送成功，方法: {Method}", method);
            }
            else
            {
                _logger.Warning("MessageService: 消息发送失败，方法: {Method}, 错误: {Error}", 
                    method, result.Error);
            }
            
            return result;
        }

        /// <summary>
        /// 获取通知消息的可观察流
        /// </summary>
        public IObservable<AcpMessage> Notifications { get; }
    }
}
