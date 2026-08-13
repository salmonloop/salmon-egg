using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Services.Chat;

namespace SalmonEgg.Presentation.Core.Services;

public sealed class ApplicationStartupWorkflow : IApplicationStartupWorkflow
{
    private readonly IShellStartupNavigationService _shellStartupNavigation;
    private readonly IChatRuntimeInitialization _chatRuntimeInitialization;
    private readonly IAppSettingsService _appSettingsService;
    private readonly ITelemetryRuntime _telemetryRuntime;
    private readonly ILogger<ApplicationStartupWorkflow> _logger;
    private readonly object _runtimeInitializationSync = new();
    private Task<bool>? _profileInitializationTask;
    private Task<bool>? _conversationRestoreTask;
    private Task? _telemetryActivationTask;
    private bool _profileInitializationCompleted;
    private bool _conversationRestoreCompleted;

    public ApplicationStartupWorkflow(
        IShellStartupNavigationService shellStartupNavigation,
        IChatRuntimeInitialization chatRuntimeInitialization,
        IAppSettingsService appSettingsService,
        ITelemetryRuntime telemetryRuntime,
        ILogger<ApplicationStartupWorkflow> logger)
    {
        _shellStartupNavigation = shellStartupNavigation ?? throw new ArgumentNullException(nameof(shellStartupNavigation));
        _chatRuntimeInitialization = chatRuntimeInitialization ?? throw new ArgumentNullException(nameof(chatRuntimeInitialization));
        _appSettingsService = appSettingsService ?? throw new ArgumentNullException(nameof(appSettingsService));
        _telemetryRuntime = telemetryRuntime ?? throw new ArgumentNullException(nameof(telemetryRuntime));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task ActivateShellAsync()
        => _shellStartupNavigation.ActivateInitialContentAsync();

    public async Task InitializeRuntimeAsync()
    {
        // 先激活遥测，再跑其余初始化：否则 profile 初始化与会话恢复这两段（最容易出问题、
        // 最需要 trace 的启动路径）会发生在没有 provider 的窗口内，永久采集不到。
        // 读配置是异步的，禁止在 App 构造函数里同步阻塞取得（启动副作用所有权约束）。
        await EnsureTelemetryActivatedAsync().ConfigureAwait(false);

        var profileInitialization = EnsureProfilesInitializedAsync();
        var conversationRestore = EnsureConversationsRestoredAsync();
        await Task.WhenAll(profileInitialization, conversationRestore).ConfigureAwait(false);
    }

    /// <summary>
    /// 用已持久化的设置激活遥测管线。共享在途任务，多个页面并发挂载只激活一次。
    /// </summary>
    /// <remarks>
    /// 不记 completed 标志：<see cref="ITelemetryRuntime.ApplyAsync"/> 本身幂等（配置未变即
    /// no-op），重试成本可忽略；而记了标志反而会让首次失败后再也不重试。
    /// </remarks>
    private Task EnsureTelemetryActivatedAsync()
    {
        lock (_runtimeInitializationSync)
        {
            if (_telemetryActivationTask is null || _telemetryActivationTask.IsCompleted)
            {
                _telemetryActivationTask = ActivateTelemetryCoreAsync();
            }

            return _telemetryActivationTask;
        }
    }

    private async Task ActivateTelemetryCoreAsync()
    {
        try
        {
            var settings = await _appSettingsService.LoadAsync().ConfigureAwait(false);
            await _telemetryRuntime.ApplyAsync(settings).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // 遥测是旁路能力，且这一步排在 profile 初始化与会话恢复之前：若让读配置失败
            // 冒泡出去，用户会因为"遥测配置读不出来"而完全打不开会话。ApplyAsync 契约上
            // 不抛，此处兜住的是 LoadAsync（如文件存储权限异常）。
            _logger.LogError(ex, "Failed to activate telemetry during startup; telemetry stays inactive");
        }
    }

    private Task<bool> EnsureProfilesInitializedAsync()
    {
        lock (_runtimeInitializationSync)
        {
            if (_profileInitializationCompleted)
            {
                return Task.FromResult(true);
            }

            if (_profileInitializationTask is null || _profileInitializationTask.IsCompleted)
            {
                _profileInitializationTask = InitializeProfilesCoreAsync();
            }

            return _profileInitializationTask;
        }
    }

    private async Task<bool> InitializeProfilesCoreAsync()
    {
        var initialized = await _chatRuntimeInitialization.InitializeAcpProfilesAsync().ConfigureAwait(false);
        if (initialized)
        {
            lock (_runtimeInitializationSync)
            {
                _profileInitializationCompleted = true;
            }
        }

        return initialized;
    }

    private Task<bool> EnsureConversationsRestoredAsync()
    {
        lock (_runtimeInitializationSync)
        {
            if (_conversationRestoreCompleted)
            {
                return Task.FromResult(true);
            }

            if (_conversationRestoreTask is null || _conversationRestoreTask.IsCompleted)
            {
                _conversationRestoreTask = RestoreConversationsCoreAsync();
            }

            return _conversationRestoreTask;
        }
    }

    private async Task<bool> RestoreConversationsCoreAsync()
    {
        var restored = await _chatRuntimeInitialization.RestoreConversationsAsync().ConfigureAwait(false);
        if (restored)
        {
            lock (_runtimeInitializationSync)
            {
                _conversationRestoreCompleted = true;
            }
        }

        return restored;
    }
}
