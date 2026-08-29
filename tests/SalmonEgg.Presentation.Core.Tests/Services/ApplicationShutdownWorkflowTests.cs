using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Application.Services.Acp;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Services.Chat;

namespace SalmonEgg.Presentation.Core.Tests.Services;

public sealed class ApplicationShutdownWorkflowTests
{
    [Fact]
    public async Task ShutdownAsync_FlushesStateThenDrainsChildProcessesThenTelemetry()
    {
        // 顺序有意义：用户状态的持久性优先于释放 OS 资源（先杀子进程再写盘会给"写盘失败"
        // 多出一个本可避免的成因），遥测最后 flush，这样上面任何失败产生的 span 仍能被处理。
        var harness = new Harness();

        await harness.Workflow.ShutdownAsync();

        Assert.Equal(
            new[] { "chat", "acp-drain", "discover", "acp-terminal", "local-terminal", "telemetry" },
            harness.Order);
    }

    [Fact]
    public async Task ShutdownAsync_DrainsEveryChildProcessOwnerExactlyOnce()
    {
        // issue #126：这四个 owner 各自持有 agent 子进程 / PTY，且都不在彼此的释放链路上——
        // 缓存会话走注册表，Discover 的浏览连接从不 RecordSession，两类终端各有自己的 manager。
        // 漏掉任何一个，那一类子进程就会被 reparent 到 init 后继续运行。
        var harness = new Harness();

        await harness.Workflow.ShutdownAsync();

        harness.ConnectionCleaner.Verify(cleaner => cleaner.DrainAllAsync(), Times.Once);
        Assert.Equal(1, harness.DiscoverFacade.DisposeAsyncCount);
        Assert.Equal(1, harness.AcpTerminals.DisposeCount);
        Assert.Equal(1, harness.LocalTerminals.DisposeAsyncCount);
    }

    [Fact]
    public async Task ShutdownAsync_WhenOneChildProcessOwnerFails_StillReleasesTheRest()
    {
        // 一个 owner 释放失败不得让其余子进程继续泄漏：这正是"关不掉的 agent"最常见的成因。
        var harness = new Harness();
        harness.ConnectionCleaner
            .Setup(cleaner => cleaner.DrainAllAsync())
            .ThrowsAsync(new InvalidOperationException("drain failed"));

        await harness.Workflow.ShutdownAsync();

        Assert.Equal(1, harness.DiscoverFacade.DisposeAsyncCount);
        Assert.Equal(1, harness.AcpTerminals.DisposeCount);
        Assert.Equal(1, harness.LocalTerminals.DisposeAsyncCount);
        harness.Telemetry.Verify(runtime => runtime.ShutdownAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ShutdownAsync_WhenChatFlushFails_StillDrainsAndShutsDownTelemetry()
    {
        // 状态 flush 失败恰恰是最需要把诊断数据送出去、也最需要收干子进程的场景；
        // 若因它抛出而跳过后续阶段，子进程会泄漏且缓冲区里的错误 span 会随进程消失。
        var harness = new Harness();
        harness.Persistence
            .Setup(p => p.FlushPendingStateAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("flush failed"));

        await harness.Workflow.ShutdownAsync();

        harness.ConnectionCleaner.Verify(cleaner => cleaner.DrainAllAsync(), Times.Once);
        harness.Telemetry.Verify(runtime => runtime.ShutdownAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ShutdownAsync_WhenCalledFromSeveralClosePaths_RunsOnce()
    {
        // 窗口关闭、托盘退出、平台生命周期都可能同时触发关闭，必须共享同一次运行，
        // 否则会并发 flush 同一批状态、并对同一批子进程重复释放。
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var harness = new Harness();
        harness.Persistence
            .Setup(p => p.FlushPendingStateAsync(It.IsAny<CancellationToken>()))
            .Returns(release.Task);

        var first = harness.Workflow.ShutdownAsync();
        var second = harness.Workflow.ShutdownAsync();
        release.SetResult();
        await Task.WhenAll(first, second);
        await harness.Workflow.ShutdownAsync();

        harness.Persistence.Verify(p => p.FlushPendingStateAsync(It.IsAny<CancellationToken>()), Times.Once);
        harness.ConnectionCleaner.Verify(cleaner => cleaner.DrainAllAsync(), Times.Once);
        Assert.Equal(1, harness.DiscoverFacade.DisposeAsyncCount);
        harness.Telemetry.Verify(runtime => runtime.ShutdownAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ShutdownAsync_WithoutLocalTerminals_StillCompletes()
    {
        // 本地 PTY 只在 desktop 注册，WASM / 移动端为 null：可选依赖缺失不得让关闭崩掉。
        var harness = new Harness(includeLocalTerminals: false);

        await harness.Workflow.ShutdownAsync();

        harness.ConnectionCleaner.Verify(cleaner => cleaner.DrainAllAsync(), Times.Once);
        harness.Telemetry.Verify(runtime => runtime.ShutdownAsync(It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(ApplicationShutdownPhase.Completed, harness.Progress.Phase);
    }

    [Fact]
    public async Task ShutdownAsync_ReportsPhasesInOrderAndEndsCompleted()
    {
        // overlay 的唯一事实源。阶段必须按执行顺序推进，且最终必须落到 Completed，
        // 否则提示会永久停在"正在关闭"。
        var harness = new Harness();

        await harness.Workflow.ShutdownAsync();

        Assert.Equal(
            new[]
            {
                ApplicationShutdownPhase.PersistingState,
                ApplicationShutdownPhase.ClosingChildProcesses,
                ApplicationShutdownPhase.Completed
            },
            harness.Progress.ReportedPhases);
        Assert.True(harness.Progress.IsShuttingDown);
    }

    [Fact]
    public async Task ShutdownAsync_WhenTelemetryThrows_StillReportsCompleted()
    {
        // 最后一段抛出时也必须落到 Completed：否则 overlay 永久挂在"正在关闭"，
        // 而进程其实已经准备退出了。
        var harness = new Harness();
        harness.Telemetry
            .Setup(runtime => runtime.ShutdownAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("telemetry shutdown failed"));

        var exception = await Record.ExceptionAsync(() => harness.Workflow.ShutdownAsync());

        Assert.Null(exception);
        Assert.Equal(ApplicationShutdownPhase.Completed, harness.Progress.Phase);
    }

    [Fact]
    public async Task ShutdownAsync_WhenCanceled_StillDrainsChildProcesses()
    {
        // 取消只能缩短"等写盘"，不能缩短"收子进程"：会话一旦脱离注册表就再无持有者，
        // 中途放弃等于把 agent 过继给 init 继续运行。
        var harness = new Harness();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        harness.Persistence
            .Setup(p => p.FlushPendingStateAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        await harness.Workflow.ShutdownAsync(cts.Token);

        harness.ConnectionCleaner.Verify(cleaner => cleaner.DrainAllAsync(), Times.Once);
        Assert.Equal(1, harness.DiscoverFacade.DisposeAsyncCount);
        Assert.Equal(1, harness.AcpTerminals.DisposeCount);
        Assert.Equal(ApplicationShutdownPhase.Completed, harness.Progress.Phase);
    }

    private sealed class Harness
    {
        public Harness(bool includeLocalTerminals = true)
        {
            Persistence = new Mock<IChatRuntimePersistence>();
            Persistence
                .Setup(p => p.FlushPendingStateAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask)
                .Callback(() => Order.Add("chat"));

            ConnectionCleaner = new Mock<IAcpConnectionSessionCleaner>();
            ConnectionCleaner
                .Setup(cleaner => cleaner.DrainAllAsync())
                .ReturnsAsync(new AcpConnectionSessionCleanupResult(2, 0))
                .Callback(() => Order.Add("acp-drain"));

            DiscoverFacade = new RecordingDiscoverFacade(Order);
            AcpTerminals = new RecordingTerminalSessionManager(Order);
            LocalTerminals = includeLocalTerminals ? new RecordingAsyncDisposable(Order) : null;

            Telemetry = new Mock<ITelemetryRuntime>();
            Telemetry
                .Setup(runtime => runtime.ShutdownAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask)
                .Callback(() => Order.Add("telemetry"));

            Workflow = new ApplicationShutdownWorkflow(
                Persistence.Object,
                ConnectionCleaner.Object,
                DiscoverFacade,
                AcpTerminals,
                Progress,
                Telemetry.Object,
                NullLogger<ApplicationShutdownWorkflow>.Instance,
                LocalTerminals);
        }

        public List<string> Order { get; } = new();

        public Mock<IChatRuntimePersistence> Persistence { get; }

        public Mock<IAcpConnectionSessionCleaner> ConnectionCleaner { get; }

        public RecordingDiscoverFacade DiscoverFacade { get; }

        public RecordingTerminalSessionManager AcpTerminals { get; }

        public RecordingAsyncDisposable? LocalTerminals { get; }

        public Mock<ITelemetryRuntime> Telemetry { get; }

        public RecordingProgressSink Progress { get; } = new();

        public ApplicationShutdownWorkflow Workflow { get; }
    }

    /// <remarks>
    /// 记录 <c>ReportPhase</c> 的完整序列而非只看末值：overlay 的正确性依赖阶段推进顺序，
    /// 只断言末值会让"跳过中间阶段"通过。
    /// </remarks>
    private sealed class RecordingProgressSink : IApplicationShutdownProgress, IApplicationShutdownProgressSink
    {
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged { add { } remove { } }

        public List<ApplicationShutdownPhase> ReportedPhases { get; } = new();

        public bool IsShuttingDown { get; private set; }

        public ApplicationShutdownPhase Phase { get; private set; } = ApplicationShutdownPhase.NotStarted;

        public void ReportPhase(ApplicationShutdownPhase phase)
        {
            ReportedPhases.Add(phase);
            if (phase != ApplicationShutdownPhase.NotStarted)
            {
                IsShuttingDown = true;
            }

            Phase = phase;
        }
    }

    private sealed class RecordingDiscoverFacade : IDiscoverSessionsConnectionFacade
    {
        private readonly List<string> _order;

        public RecordingDiscoverFacade(List<string> order) => _order = order;

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged { add { } remove { } }

        public int DisposeAsyncCount { get; private set; }

        public bool IsConnecting => false;

        public bool IsInitializing => false;

        public bool IsConnected => false;

        public string? ConnectionErrorMessage => null;

        public SalmonEgg.Application.Services.Chat.IChatService? CurrentChatService => null;

        public Task ConnectToProfileAsync(SalmonEgg.Domain.Models.ServerConfiguration profile)
            => Task.CompletedTask;

        public ValueTask DisposeAsync()
        {
            DisposeAsyncCount++;
            _order.Add("discover");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingTerminalSessionManager : ITerminalSessionManager
    {
        private readonly List<string> _order;

        public RecordingTerminalSessionManager(List<string> order) => _order = order;

        public int DisposeCount { get; private set; }

        public void Dispose()
        {
            DisposeCount++;
            _order.Add("acp-terminal");
        }

        public Task<TerminalCreateResponse> CreateAsync(TerminalCreateRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<TerminalOutputResponse> GetOutputAsync(TerminalOutputRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<TerminalWaitForExitResponse> WaitForExitAsync(TerminalWaitForExitRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<TerminalKillResponse> KillAsync(TerminalKillRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<TerminalReleaseResponse> ReleaseAsync(TerminalReleaseRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class RecordingAsyncDisposable : IAsyncDisposable
    {
        private readonly List<string> _order;

        public RecordingAsyncDisposable(List<string> order) => _order = order;

        public int DisposeAsyncCount { get; private set; }

        public ValueTask DisposeAsync()
        {
            DisposeAsyncCount++;
            _order.Add("local-terminal");
            return ValueTask.CompletedTask;
        }
    }
}
