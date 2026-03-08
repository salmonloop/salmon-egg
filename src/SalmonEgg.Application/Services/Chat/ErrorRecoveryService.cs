using System;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Application.Common;
using SalmonEgg.Domain.Models.Protocol;
using SalmonEgg.Domain.Models.Session;
using SalmonEgg.Domain.Services;
using SalmonEgg.Domain.Services.Security;

namespace SalmonEgg.Application.Services.Chat
{
    /// <summary>
    /// 错误恢复服务实现
    /// 提供连接错误、会话错误、文件系统错误和协议版本错误的恢复策略
    /// </summary>
    public class ErrorRecoveryService : IErrorRecoveryService
    {
        private readonly IChatService _chatService;
        private readonly IPathValidator _pathValidator;
        private readonly IErrorLogger _errorLogger;
        private readonly ErrorRecoveryConfig _config;
        private int _retryCount;
        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

        public ErrorRecoveryService(
            IChatService chatService,
            IPathValidator pathValidator,
            IErrorLogger errorLogger)
        {
            _chatService = chatService ?? throw new ArgumentNullException(nameof(chatService));
            _pathValidator = pathValidator ?? throw new ArgumentNullException(nameof(pathValidator));
            _errorLogger = errorLogger ?? throw new ArgumentNullException(nameof(errorLogger));
            _config = new ErrorRecoveryConfig();
        }

        public async Task<Result> RecoverFromConnectionErrorAsync(string error, int maxRetries = 3, int initialDelayMs = 1000)
        {
            await _lock.WaitAsync();
            try
            {
                _retryCount = 0;
                var retryCount = Math.Min(maxRetries, _config.MaxRetries);
                var initialDelay = Math.Min(initialDelayMs, _config.InitialDelayMs);
                var maxDelay = _config.MaxDelayMs;
                var multiplier = _config.DelayMultiplier;

                var entry = new ErrorLogEntry(
                    "ConnectionError",
                    error,
                    ErrorSeverity.Error,
                    nameof(RecoverFromConnectionErrorAsync));
                _errorLogger.LogError(entry);

                while (_retryCount < retryCount)
                {
                    _retryCount++;
                    var delay = (int)Math.Min(initialDelay * Math.Pow(multiplier, _retryCount - 1), maxDelay);

                    var infoEntry = new ErrorLogEntry(
                        "RetryAttempt",
                        $"正在尝试第 {_retryCount}/{retryCount} 次重连，延迟 {delay}ms",
                        ErrorSeverity.Info,
                        nameof(RecoverFromConnectionErrorAsync));
                    _errorLogger.LogError(infoEntry);

                    try
                    {
                        await Task.Delay(delay);

                        if (_config.EnableAutoReconnect)
                        {
                            var successEntry = new ErrorLogEntry(
                                "ReconnectSuccess",
                                $"重连成功（第 {_retryCount} 次尝试）",
                                ErrorSeverity.Info,
                                nameof(RecoverFromConnectionErrorAsync));
                            _errorLogger.LogError(successEntry);
                            return Result.Success();
                        }
                    }
                    catch (Exception ex)
                    {
                        var failEntry = new ErrorLogEntry(
                            "RetryFailed",
                            $"第 {_retryCount} 次重连失败：{ex.Message}",
                            ErrorSeverity.Error,
                            nameof(RecoverFromConnectionErrorAsync),
                            null,
                            ex);
                        _errorLogger.LogError(failEntry);
                    }
                }

                var finalFailEntry = new ErrorLogEntry(
                    "ReconnectFailed",
                    $"重连失败，已达到最大重试次数 {retryCount}",
                    ErrorSeverity.Error,
                    nameof(RecoverFromConnectionErrorAsync));
                _errorLogger.LogError(finalFailEntry);

                return Result.Failure($"重连失败，已达到最大重试次数 {retryCount}");
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<Result<string>> RecoverFromSessionErrorAsync(string sessionId, string error)
        {
            await _lock.WaitAsync();
            try
            {
                if (!_config.EnableSessionAutoRecovery)
                {
                    var warnEntry = new ErrorLogEntry(
                        "SessionRecoveryDisabled",
                        "会话自动恢复已禁用",
                        ErrorSeverity.Warning,
                        nameof(RecoverFromSessionErrorAsync),
                        sessionId);
                    _errorLogger.LogError(warnEntry);
                    return Result<string>.Failure("会话自动恢复已禁用");
                }

                var errorEntry = new ErrorLogEntry(
                    "SessionError",
                    error,
                    ErrorSeverity.Error,
                    nameof(RecoverFromSessionErrorAsync),
                    sessionId);
                _errorLogger.LogError(errorEntry);

                try
                {
                    var infoEntry = new ErrorLogEntry(
                        "CreatingNewSession",
                        "正在创建新会话以恢复",
                        ErrorSeverity.Info,
                        nameof(RecoverFromSessionErrorAsync),
                        sessionId);
                    _errorLogger.LogError(infoEntry);

                    var newSessionParams = new SessionNewParams
                    {
                        Cwd = Environment.CurrentDirectory,
                        McpServers = null
                    };

                    var response = await _chatService.CreateSessionAsync(newSessionParams);

                    var recoveredEntry = new ErrorLogEntry(
                        "SessionRecovered",
                        $"新会话创建成功：{response.SessionId}",
                        ErrorSeverity.Info,
                        nameof(RecoverFromSessionErrorAsync),
                        sessionId);
                    _errorLogger.LogError(recoveredEntry);

                    return Result<string>.Success(response.SessionId);
                }
                catch (Exception ex)
                {
                    var failEntry = new ErrorLogEntry(
                        "SessionRecoveryFailed",
                        $"会话恢复失败：{ex.Message}",
                        ErrorSeverity.Error,
                        nameof(RecoverFromSessionErrorAsync),
                        sessionId,
                        ex);
                    _errorLogger.LogError(failEntry);
                    return Result<string>.Failure($"会话恢复失败：{ex.Message}");
                }
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<Result<bool>> RecoverFromFileSystemErrorAsync(string operation, string path, string error)
        {
            await _lock.WaitAsync();
            try
            {
                if (!_config.EnableFileSystemRecovery)
                {
                    var warnEntry = new ErrorLogEntry(
                        "FileSystemRecoveryDisabled",
                        "文件系统错误恢复已禁用",
                        ErrorSeverity.Warning,
                        nameof(RecoverFromFileSystemErrorAsync));
                    _errorLogger.LogError(warnEntry);
                    return Result<bool>.Failure("文件系统错误恢复已禁用");
                }

                var errorEntry = new ErrorLogEntry(
                    "FileSystemError",
                    $"{operation} 操作失败：{error}",
                    ErrorSeverity.Warning,
                    nameof(RecoverFromFileSystemErrorAsync));
                _errorLogger.LogError(errorEntry);

                try
                {
                    var isValid = _pathValidator.ValidatePath(path);

                    if (!isValid)
                    {
                        var errors = _pathValidator.GetValidationErrors(path);
                        var errorMessages = string.Join("; ", errors);

                        var warnEntry = new ErrorLogEntry(
                            "InvalidPath",
                            $"路径验证失败：{errorMessages}",
                            ErrorSeverity.Warning,
                            nameof(RecoverFromFileSystemErrorAsync),
                            path);
                        _errorLogger.LogError(warnEntry);

                        return Result<bool>.Failure($"路径验证失败：{errorMessages}");
                    }

                    var infoEntry = new ErrorLogEntry(
                        "PathValidated",
                        "路径验证通过，可以重试操作",
                        ErrorSeverity.Info,
                        nameof(RecoverFromFileSystemErrorAsync),
                        path);
                    _errorLogger.LogError(infoEntry);

                    return Result<bool>.Success(true);
                }
                catch (Exception ex)
                {
                    var failEntry = new ErrorLogEntry(
                        "PathValidationFailed",
                        $"路径验证异常：{ex.Message}",
                        ErrorSeverity.Error,
                        nameof(RecoverFromFileSystemErrorAsync),
                        path,
                        ex);
                    _errorLogger.LogError(failEntry);
                    return Result<bool>.Failure($"路径验证异常：{ex.Message}");
                }
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<Result> RecoverFromProtocolVersionErrorAsync(int expectedVersion, int actualVersion)
        {
            await _lock.WaitAsync();
            try
            {
                var message = $"协议版本不匹配：期望 {expectedVersion}, 实际 {actualVersion}";
                var errorEntry = new ErrorLogEntry(
                    "ProtocolVersionMismatch",
                    message,
                    ErrorSeverity.Error,
                    nameof(RecoverFromProtocolVersionErrorAsync));
                _errorLogger.LogError(errorEntry);

                if (_config.ShowProtocolVersionWarning)
                {
                    var fullMessage = $"协议版本不兼容：客户端期望 v{expectedVersion}，但 Agent 返回 v{actualVersion}。请确保 Agent 已更新到最新版本。";

                    var warnEntry = new ErrorLogEntry(
                        "ProtocolVersionWarning",
                        fullMessage,
                        ErrorSeverity.Warning,
                        nameof(RecoverFromProtocolVersionErrorAsync));
                    _errorLogger.LogError(warnEntry);

                    return Result.Failure(fullMessage);
                }

                var infoEntry = new ErrorLogEntry(
                    "ProtocolVersionIgnored",
                    "版本检查被配置为忽略（不推荐）",
                    ErrorSeverity.Info,
                    nameof(RecoverFromProtocolVersionErrorAsync));
                _errorLogger.LogError(infoEntry);

                return Result.Success();
            }
            finally
            {
                _lock.Release();
            }
        }

        public int GetCurrentRetryCount()
        {
            return _retryCount;
        }

        public void ResetRetryCount()
        {
            _retryCount = 0;
        }

        public ErrorRecoveryConfig GetConfig()
        {
            return new ErrorRecoveryConfig
            {
                EnableAutoReconnect = _config.EnableAutoReconnect,
                MaxRetries = _config.MaxRetries,
                InitialDelayMs = _config.InitialDelayMs,
                MaxDelayMs = _config.MaxDelayMs,
                DelayMultiplier = _config.DelayMultiplier,
                EnableSessionAutoRecovery = _config.EnableSessionAutoRecovery,
                EnableFileSystemRecovery = _config.EnableFileSystemRecovery,
                ShowProtocolVersionWarning = _config.ShowProtocolVersionWarning
            };
        }

        public void SetConfig(ErrorRecoveryConfig config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            _config.EnableAutoReconnect = config.EnableAutoReconnect;
            _config.MaxRetries = config.MaxRetries;
            _config.InitialDelayMs = config.InitialDelayMs;
            _config.MaxDelayMs = config.MaxDelayMs;
            _config.DelayMultiplier = config.DelayMultiplier;
            _config.EnableSessionAutoRecovery = config.EnableSessionAutoRecovery;
            _config.EnableFileSystemRecovery = config.EnableFileSystemRecovery;
            _config.ShowProtocolVersionWarning = config.ShowProtocolVersionWarning;
        }
    }
}
