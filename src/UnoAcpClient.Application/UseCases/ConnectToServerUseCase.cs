using System;
using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using Serilog;
using UnoAcpClient.Application.Common;
using UnoAcpClient.Application.Services;
using UnoAcpClient.Domain.Models;
using UnoAcpClient.Domain.Services;

namespace UnoAcpClient.Application.UseCases
{
    /// <summary>
    /// 连接到服务器用例
    /// 负责加载配置、验证配置、建立连接并记录日志
    /// </summary>
    public class ConnectToServerUseCase
    {
        private readonly IConnectionManager _connectionManager;
        private readonly IConfigurationService _configService;
        private readonly ILogger _logger;
        private readonly IValidator<ServerConfiguration> _validator;

        /// <summary>
        /// 初始化 ConnectToServerUseCase 的新实例
        /// </summary>
        /// <param name="connectionManager">连接管理器</param>
        /// <param name="configService">配置服务</param>
        /// <param name="logger">日志记录器</param>
        /// <param name="validator">配置验证器</param>
        public ConnectToServerUseCase(
            IConnectionManager connectionManager,
            IConfigurationService configService,
            ILogger logger,
            IValidator<ServerConfiguration> validator)
        {
            _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        }

        /// <summary>
        /// 执行连接到服务器的操作
        /// </summary>
        /// <param name="configId">服务器配置 ID</param>
        /// <returns>操作结果</returns>
        public async Task<Result> ExecuteAsync(string configId)
        {
            try
            {
                // 验证输入参数 (Requirement 3.2)
                if (string.IsNullOrWhiteSpace(configId))
                {
                    _logger.Warning("连接失败：配置 ID 为空");
                    return Result.Failure("配置 ID 不能为空");
                }

                _logger.Information("开始连接到服务器，配置 ID: {ConfigId}", configId);

                // 1. 加载配置 (Requirement 5.1)
                var config = await _configService.LoadConfigurationAsync(configId);
                if (config == null)
                {
                    _logger.Warning("连接失败：未找到配置 ID {ConfigId}", configId);
                    return Result.Failure($"未找到配置 ID: {configId}");
                }

                _logger.Debug("成功加载配置: {ConfigName} ({ServerUrl})", config.Name, config.ServerUrl);

                // 2. 验证配置 (Requirement 3.2, 使用 FluentValidation)
                var validationResult = await _validator.ValidateAsync(config);
                if (!validationResult.IsValid)
                {
                    var errors = string.Join("; ", validationResult.Errors);
                    _logger.Warning("配置验证失败: {Errors}", errors);
                    return Result.Failure($"配置验证失败: {errors}");
                }

                _logger.Debug("配置验证通过");

                // 3. 建立连接 (Requirement 3.1)
                var connectionResult = await _connectionManager.ConnectAsync(config, CancellationToken.None);
                
                if (!connectionResult.IsSuccess)
                {
                    // 连接失败时返回具体的错误原因 (Requirement 3.2)
                    _logger.Error("连接到服务器失败: {Error}", connectionResult.Error);
                    return Result.Failure(connectionResult.Error ?? "Connection failed");
                }

                // 4. 记录日志 (Requirement 6.1)
                _logger.Information("成功连接到服务器 {ServerUrl}", config.ServerUrl);

                return Result.Success();
            }
            catch (Exception ex)
            {
                // 记录详细的错误日志 (Requirement 6.1)
                _logger.Error(ex, "连接到服务器时发生未预期的错误，配置 ID: {ConfigId}", configId);
                return Result.Failure("连接失败：发生未预期的错误");
            }
        }
    }
}
