using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Services.Chat;

namespace SalmonEgg.Presentation.Core.Tests.Services;

public sealed class ApplicationShutdownWorkflowTests
{
    [Fact]
    public async Task ShutdownAsync_FlushesChatStateThenTelemetry()
    {
        // 顺序有意义：用户状态的持久性优先，遥测最后 flush，这样上面任何失败产生的 span
        // 仍能进入这次导出。
        var order = new List<string>();
        var persistence = new Mock<IChatRuntimePersistence>();
        persistence
            .Setup(p => p.FlushPendingStateAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Callback(() => order.Add("chat"));
        var telemetry = new Mock<ITelemetryRuntime>();
        telemetry
            .Setup(runtime => runtime.ShutdownAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Callback(() => order.Add("telemetry"));

        var workflow = new ApplicationShutdownWorkflow(
            persistence.Object,
            telemetry.Object,
            NullLogger<ApplicationShutdownWorkflow>.Instance);

        await workflow.ShutdownAsync();

        Assert.Equal(new[] { "chat", "telemetry" }, order);
    }

    [Fact]
    public async Task ShutdownAsync_WhenChatFlushFails_StillShutsDownTelemetry()
    {
        // 状态 flush 失败恰恰是最需要把诊断数据送出去的场景；若因它抛出而跳过遥测
        // shutdown，缓冲区里的错误 span 会随进程一起消失。
        var persistence = new Mock<IChatRuntimePersistence>();
        persistence
            .Setup(p => p.FlushPendingStateAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("flush failed"));
        var telemetry = new Mock<ITelemetryRuntime>();
        telemetry
            .Setup(runtime => runtime.ShutdownAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var workflow = new ApplicationShutdownWorkflow(
            persistence.Object,
            telemetry.Object,
            NullLogger<ApplicationShutdownWorkflow>.Instance);

        await workflow.ShutdownAsync();

        telemetry.Verify(runtime => runtime.ShutdownAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ShutdownAsync_WhenCalledFromSeveralClosePaths_RunsOnce()
    {
        // 窗口关闭、托盘退出、平台生命周期都可能同时触发关闭，必须共享同一次运行，
        // 否则会并发 flush 同一批状态。
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var persistence = new Mock<IChatRuntimePersistence>();
        persistence
            .Setup(p => p.FlushPendingStateAsync(It.IsAny<CancellationToken>()))
            .Returns(release.Task);
        var telemetry = new Mock<ITelemetryRuntime>();
        telemetry
            .Setup(runtime => runtime.ShutdownAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var workflow = new ApplicationShutdownWorkflow(
            persistence.Object,
            telemetry.Object,
            NullLogger<ApplicationShutdownWorkflow>.Instance);

        var first = workflow.ShutdownAsync();
        var second = workflow.ShutdownAsync();
        release.SetResult();
        await Task.WhenAll(first, second);
        await workflow.ShutdownAsync();

        persistence.Verify(p => p.FlushPendingStateAsync(It.IsAny<CancellationToken>()), Times.Once);
        telemetry.Verify(runtime => runtime.ShutdownAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
