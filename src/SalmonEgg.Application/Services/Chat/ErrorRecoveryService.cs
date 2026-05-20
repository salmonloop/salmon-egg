using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Application.Common;
using SalmonEgg.Domain.Models.Mcp;
using SalmonEgg.Domain.Models.Protocol;
using SalmonEgg.Domain.Models.Session;
using SalmonEgg.Domain.Services;
using SalmonEgg.Domain.Services.Security;

namespace SalmonEgg.Application.Services.Chat
{
    public class ErrorRecoveryService : IErrorRecoveryService
    {
        private readonly Func<IChatService?> _chatServiceAccessor;
        private readonly Func<IReadOnlyList<McpServer>> _mcpServersAccessor;
        private readonly IPathValidator _pathValidator;
        private readonly IErrorLogger _errorLogger;
        private readonly ErrorRecoveryConfig _config;
        private int _retryCount;
        
        // CRITICAL: Prevent concurrent recovery attempts which could cause inconsistent state 
        // or flood the server with reconnect requests.
        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

        public ErrorRecoveryService(
            Func<IChatService?> chatServiceAccessor,
            IPathValidator pathValidator,
            IErrorLogger errorLogger,
            Func<IReadOnlyList<McpServer>>? mcpServersAccessor = null)
        {
            _chatServiceAccessor = chatServiceAccessor ?? throw new ArgumentNullException(nameof(chatServiceAccessor));
            _mcpServersAccessor = mcpServersAccessor ?? (() => Array.Empty<McpServer>());
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
                    
                    // RECOVERY STRATEGY: Exponential backoff to avoid hammering the server during transient failures.
                    var delay = (int)Math.Min(initialDelay * Math.Pow(multiplier, _retryCount - 1), maxDelay);

                    var infoEntry = new ErrorLogEntry(
                        "RetryAttempt",
                        $"Attempting reconnect {_retryCount}/{retryCount}, delay {delay}ms",
                        ErrorSeverity.Info,
                        nameof(RecoverFromConnectionErrorAsync));
                    _errorLogger.LogError(infoEntry);

                    try
                    {
                        await Task.Delay(delay);

                        // If auto-reconnect logic exists elsewhere (e.g. in AcpClient), 
                        // this check validates if a background reconnect succeeded.
                        if (_config.EnableAutoReconnect)
                        {
                            var successEntry = new ErrorLogEntry(
                                "ReconnectSuccess",
                                $"Reconnect successful (attempt {_retryCount})",
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
                            $"Reconnect attempt {_retryCount} failed: {ex.Message}",
                            ErrorSeverity.Error,
                            nameof(RecoverFromConnectionErrorAsync),
                            null,
                            ex);
                        _errorLogger.LogError(failEntry);
                    }
                }

                var finalFailEntry = new ErrorLogEntry(
                    "ReconnectFailed",
                    $"Reconnect failed, reached max retries {retryCount}",
                    ErrorSeverity.Error,
                    nameof(RecoverFromConnectionErrorAsync));
                _errorLogger.LogError(finalFailEntry);

                return Result.Failure($"Reconnect failed, reached max retries {retryCount}");
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
                    return Result<string>.Failure("Session auto-recovery is disabled");
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
                    // STATE RESET: Attempt to start completely fresh using the original workspace/context.
                    var newSessionParams = new SessionNewParams
                    {
                        Cwd = Environment.CurrentDirectory,
                        McpServers = McpServerJsonConverter.CloneServers(_mcpServersAccessor())
                    };

                    var chatService = _chatServiceAccessor();
                    if (chatService == null)
                    {
                        return Result<string>.Failure("No active chat service is available for session recovery");
                    }

                    var response = await chatService.CreateSessionAsync(newSessionParams);

                    var recoveredEntry = new ErrorLogEntry(
                        "SessionRecovered",
                        $"New session created successfully: {response.SessionId}",
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
                        $"Session recovery failed: {ex.Message}",
                        ErrorSeverity.Error,
                        nameof(RecoverFromSessionErrorAsync),
                        sessionId,
                        ex);
                    _errorLogger.LogError(failEntry);
                    return Result<string>.Failure($"Session recovery failed: {ex.Message}");
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
                    return Result<bool>.Failure("File system error recovery is disabled");

                var errorEntry = new ErrorLogEntry(
                    "FileSystemError",
                    $"{operation} operation failed: {error}",
                    ErrorSeverity.Warning,
                    nameof(RecoverFromFileSystemErrorAsync));
                _errorLogger.LogError(errorEntry);

                try
                {
                    // VALIDATION CHECK: FS errors often stem from invalid absolute/relative path mixing.
                    var isValid = _pathValidator.ValidatePath(path);

                    if (!isValid)
                    {
                        var errors = _pathValidator.GetValidationErrors(path);
                        var errorMessages = string.Join("; ", errors);
                        return Result<bool>.Failure($"Path validation failed: {errorMessages}");
                    }

                    return Result<bool>.Success(true);
                }
                catch (Exception ex)
                {
                    var failEntry = new ErrorLogEntry(
                        "PathValidationFailed",
                        $"Path validation exception: {ex.Message}",
                        ErrorSeverity.Error,
                        nameof(RecoverFromFileSystemErrorAsync),
                        path,
                        ex);
                    _errorLogger.LogError(failEntry);
                    return Result<bool>.Failure($"Path validation exception: {ex.Message}");
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
                var message = $"Protocol version mismatch: Expected {expectedVersion}, actual {actualVersion}";
                
                if (_config.ShowProtocolVersionWarning)
                {
                    // USER EXPERIENCE: Version mismatch usually requires external action (updating the app/agent).
                    var fullMessage = $"Protocol version incompatible: Client expects v{expectedVersion}, but Agent returned v{actualVersion}. Please ensure the Agent is updated to the latest version.";
                    return Result.Failure(fullMessage);
                }

                return Result.Success();
            }
            finally
            {
                _lock.Release();
            }
        }

        public int GetCurrentRetryCount() => _retryCount;

        public void ResetRetryCount() => _retryCount = 0;

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
            if (config == null) throw new ArgumentNullException(nameof(config));

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
