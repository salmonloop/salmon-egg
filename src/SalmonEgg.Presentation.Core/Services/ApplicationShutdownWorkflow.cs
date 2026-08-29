using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SalmonEgg.Application.Services.Acp;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Services.Chat;

namespace SalmonEgg.Presentation.Core.Services;

public sealed class ApplicationShutdownWorkflow : IApplicationShutdownWorkflow
{
    private readonly IChatRuntimePersistence _chatRuntimePersistence;
    private readonly IAcpConnectionSessionCleaner _connectionSessionCleaner;
    private readonly IDiscoverSessionsConnectionFacade _discoverConnectionFacade;
    private readonly ITerminalSessionManager _terminalSessionManager;
    private readonly IAsyncDisposable? _localTerminalSessions;
    private readonly IApplicationShutdownProgressSink _progressSink;
    private readonly ITelemetryRuntime _telemetryRuntime;
    private readonly ILogger<ApplicationShutdownWorkflow> _logger;
    private readonly object _shutdownSync = new();
    private Task? _shutdownTask;

    /// <param name="localTerminalSessions">
    /// 本地交互式终端（PTY）的释放入口，仅 desktop 注册，其余平台为 null。
    /// 声明为 <see cref="IAsyncDisposable"/> 而非具体类型：本层只需要"能异步释放"这一契约，
    /// 依赖具体协调器类型会把仅存在于部分平台的实现拖进所有平台的构造签名。
    /// </param>
    public ApplicationShutdownWorkflow(
        IChatRuntimePersistence chatRuntimePersistence,
        IAcpConnectionSessionCleaner connectionSessionCleaner,
        IDiscoverSessionsConnectionFacade discoverConnectionFacade,
        ITerminalSessionManager terminalSessionManager,
        IApplicationShutdownProgressSink progressSink,
        ITelemetryRuntime telemetryRuntime,
        ILogger<ApplicationShutdownWorkflow> logger,
        IAsyncDisposable? localTerminalSessions = null)
    {
        _chatRuntimePersistence = chatRuntimePersistence ?? throw new ArgumentNullException(nameof(chatRuntimePersistence));
        _connectionSessionCleaner = connectionSessionCleaner ?? throw new ArgumentNullException(nameof(connectionSessionCleaner));
        _discoverConnectionFacade = discoverConnectionFacade ?? throw new ArgumentNullException(nameof(discoverConnectionFacade));
        _terminalSessionManager = terminalSessionManager ?? throw new ArgumentNullException(nameof(terminalSessionManager));
        _progressSink = progressSink ?? throw new ArgumentNullException(nameof(progressSink));
        _telemetryRuntime = telemetryRuntime ?? throw new ArgumentNullException(nameof(telemetryRuntime));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _localTerminalSessions = localTerminalSessions;
    }

    public Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        lock (_shutdownSync)
        {
            // Several close paths can race to end the process; they all join the same run so state is
            // flushed once. The completed task is kept so late callers return immediately.
            _shutdownTask ??= ShutdownCoreAsync(cancellationToken);
            return _shutdownTask;
        }
    }

    private async Task ShutdownCoreAsync(CancellationToken cancellationToken)
    {
        // 进度只由本 owner 置位。finally 保证异常路径也会落到 Completed，
        // 否则 overlay 会永久停在"正在关闭"。
        var stopwatch = Stopwatch.StartNew();
        try
        {
            // 顺序有意义：用户状态的持久性优先于释放 OS 资源——先杀子进程再写盘，
            // 会让"写盘失败"多出一个本可避免的成因。
            _progressSink.ReportPhase(ApplicationShutdownPhase.PersistingState);
            await RunStageAsync(
                () => _chatRuntimePersistence.FlushPendingStateAsync(cancellationToken),
                "Application shutdown flush failed",
                cancellationToken).ConfigureAwait(false);

            // 子进程释放不接 cancellationToken：这些进程一旦脱离注册表就再无持有者，
            // 中途放弃等于把它们过继给 init 继续运行（issue #126 的原始症状）。
            _progressSink.ReportPhase(ApplicationShutdownPhase.ClosingChildProcesses);
            await RunStageAsync(
                DrainChildProcessesAsync,
                "Application shutdown child-process drain failed",
                cancellationToken: default).ConfigureAwait(false);

            // Telemetry is last and unconditional: user state durability comes first, and a failure
            // above is exactly the kind of event whose spans must still reach the backend. Shutdown no
            // longer waits for export (see ITelemetryRuntime.ShutdownAsync), so this cannot stall exit.
            // Hosts must not reach into the telemetry manager themselves — teardown has one owner.
            //
            // Wrapped like every other stage rather than awaited bare: today's ITelemetryRuntime
            // implementation swallows its own failures, but this workflow runs inside a platform close
            // handler and must not depend on a downstream class continuing to do that. Leaving it bare
            // makes "teardown never throws at the host" a property of another type's catch block.
            await RunStageAsync(
                () => _telemetryRuntime.ShutdownAsync(cancellationToken),
                "Application shutdown telemetry teardown failed",
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            stopwatch.Stop();
            _logger.LogInformation(
                "Application shutdown completed. elapsedMs={ElapsedMilliseconds}",
                stopwatch.ElapsedMilliseconds);
            _progressSink.ReportPhase(ApplicationShutdownPhase.Completed);
        }
    }

    /// <summary>
    /// 终止本进程启动的所有子进程：缓存的 ACP agent、Discover 浏览连接、ACP 终端、本地 PTY。
    /// </summary>
    /// <remarks>
    /// 这些 owner 都实现了正确的释放（<c>Kill(entireProcessTree: true)</c>），但在此之前
    /// 关闭路径无人调用它们——DI 容器从不 Dispose，所以 singleton 的 <c>Dispose()</c>
    /// 在退出时是死代码。此处逐个显式释放，而不是改为 dispose 整个容器：WinUI 头在
    /// teardown 全程窗口仍可见且在绑定，dispose ViewModel singleton 会拆掉活着的 UI。
    ///
    /// 各段彼此独立 try/catch：任一 owner 失败不得让其余子进程继续泄漏。
    /// </remarks>
    private async Task DrainChildProcessesAsync()
    {
        var drainResult = await TryRunAsync(
            _connectionSessionCleaner.DrainAllAsync,
            "Failed to drain cached ACP connection sessions during shutdown").ConfigureAwait(false);
        if (drainResult is { } result && (result.RemovedCount > 0 || result.DisposeFailureCount > 0))
        {
            _logger.LogInformation(
                "Drained cached ACP sessions during shutdown. removedCount={RemovedCount} disposeFailureCount={DisposeFailureCount}",
                result.RemovedCount,
                result.DisposeFailureCount);
        }

        // Discover 的浏览连接自持 IChatService 且从不进注册表，registry 式 drain 抓不到它。
        await TryRunAsync(
            _discoverConnectionFacade.DisposeAsync().AsTask,
            "Failed to dispose the Discover ACP browse connection during shutdown").ConfigureAwait(false);

        await TryRunAsync(
            () =>
            {
                _terminalSessionManager.Dispose();
                return Task.CompletedTask;
            },
            "Failed to dispose ACP terminal sessions during shutdown").ConfigureAwait(false);

        if (_localTerminalSessions is not null)
        {
            await TryRunAsync(
                _localTerminalSessions.DisposeAsync().AsTask,
                "Failed to dispose local terminal sessions during shutdown").ConfigureAwait(false);
        }
    }

    private async Task RunStageAsync(Func<Task> stage, string failureMessage, CancellationToken cancellationToken)
    {
        try
        {
            await stage().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Application shutdown stage was canceled; pending work may not have completed");
        }
        catch (Exception ex)
        {
            // Teardown must not throw into a platform close handler: a failed stage should not also
            // block the window from closing.
            _logger.LogError(ex, "{FailureMessage}", failureMessage);
        }
    }

    private async Task<T?> TryRunAsync<T>(Func<Task<T>> operation, string failureMessage)
        where T : struct
    {
        try
        {
            return await operation().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{FailureMessage}", failureMessage);
            return null;
        }
    }

    private async Task TryRunAsync(Func<Task> operation, string failureMessage)
    {
        try
        {
            await operation().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{FailureMessage}", failureMessage);
        }
    }
}
