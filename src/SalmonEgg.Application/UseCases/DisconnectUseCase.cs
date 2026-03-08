using System;
using System.Threading.Tasks;
using Serilog;
using SalmonEgg.Application.Common;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Application.UseCases
{
    /// <summary>
    /// 断开连接用例
    /// 负责断开与服务器的连接并记录日志
    /// </summary>
    public class DisconnectUseCase
    {
        private readonly IConnectionManager _connectionManager;
        private readonly ILogger _logger;

        /// <summary>
        /// 初始化 DisconnectUseCase 的新实例
        /// </summary>
        /// <param name="connectionManager">连接管理器</param>
        /// <param name="logger">日志记录器</param>
        public DisconnectUseCase(
            IConnectionManager connectionManager,
            ILogger logger)
        {
            _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// 执行断开连接的操作
        /// </summary>
        /// <returns>操作结果</returns>
        public async Task<Result> ExecuteAsync()
        {
            try
            {
                _logger.Information("开始断开与服务器的连接");

                // 断开连接 (Requirement 3.1)
                await _connectionManager.DisconnectAsync();

                // 记录日志 (Requirement 6.1)
                _logger.Information("成功断开与服务器的连接");

                return Result.Success();
            }
            catch (Exception ex)
            {
                // 记录详细的错误日志 (Requirement 6.1)
                _logger.Error(ex, "断开连接时发生未预期的错误");
                return Result.Failure("断开连接失败：发生未预期的错误");
            }
        }
    }
}
