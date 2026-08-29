using System;
using System.Threading.Tasks;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Tests.Threading;
using SalmonEgg.Presentation.ViewModels.Navigation;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Navigation;

/// <summary>
/// ShellShutdownOverlayViewModel 的投影行为：阈值前不出场、超阈值出场、
/// 完成即取消挂起的显示计时、文案随阶段映射。
/// </summary>
public sealed class ShellShutdownOverlayViewModelTests
{
    private static readonly TimeSpan Threshold = TimeSpan.FromMilliseconds(
        ShellShutdownOverlayViewModel.RevealThresholdMilliseconds);

    [Fact]
    public async Task BelowThreshold_ShowsNothing()
    {
        var store = new ApplicationShutdownProgressStore();
        using var sut = new ShellShutdownOverlayViewModel(store, new ImmediateUiDispatcher());

        store.ReportPhase(ApplicationShutdownPhase.PersistingState);

        await Task.Delay(Threshold / 2, TestContext.Current.CancellationToken);
        Assert.False(sut.IsOverlayVisible);
    }

    [Fact]
    public async Task AboveThreshold_RevealsOverlay()
    {
        var store = new ApplicationShutdownProgressStore();
        using var sut = new ShellShutdownOverlayViewModel(store, new ImmediateUiDispatcher());

        store.ReportPhase(ApplicationShutdownPhase.ClosingChildProcesses);

        // 阈值计时器自身还有调度抖动，多留一截余量，别让测试去撞调度器的下限。
        await Task.Delay(Threshold + TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);
        Assert.True(sut.IsOverlayVisible);
    }

    [Fact]
    public async Task CompletedBeforeThreshold_CancelPendingReveal()
    {
        var store = new ApplicationShutdownProgressStore();
        using var sut = new ShellShutdownOverlayViewModel(store, new ImmediateUiDispatcher());

        store.ReportPhase(ApplicationShutdownPhase.PersistingState);
        store.ReportPhase(ApplicationShutdownPhase.Completed);

        await Task.Delay(Threshold + TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);
        Assert.False(sut.IsOverlayVisible);
    }

    [Fact]
    public async Task RevealTimer_StartsOnceAcrossPhaseChanges()
    {
        var store = new ApplicationShutdownProgressStore();
        using var sut = new ShellShutdownOverlayViewModel(store, new ImmediateUiDispatcher());

        // 连续两个阶段都会尝试起计时；实现里第二次必须被去重，
        // 否则每个阶段各挂一个计时器，触发次数随阶段数增长。
        store.ReportPhase(ApplicationShutdownPhase.PersistingState);
        store.ReportPhase(ApplicationShutdownPhase.ClosingChildProcesses);

        await Task.Delay(Threshold + TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);
        Assert.True(sut.IsOverlayVisible);
    }

    [Fact]
    public void StatusText_MapsPhase()
    {
        var store = new ApplicationShutdownProgressStore();
        using var sut = new ShellShutdownOverlayViewModel(store, new ImmediateUiDispatcher());

        Assert.False(string.IsNullOrWhiteSpace(sut.StatusText));

        store.ReportPhase(ApplicationShutdownPhase.PersistingState);
        Assert.Equal("Saving conversations...", sut.StatusText);

        store.ReportPhase(ApplicationShutdownPhase.ClosingChildProcesses);
        Assert.Equal("Closing agent processes...", sut.StatusText);

        store.ReportPhase(ApplicationShutdownPhase.Completed);
        Assert.Equal("Shutting down...", sut.StatusText);
    }

    [Fact]
    public void CompletedPhase_IsNeverRevealed()
    {
        var store = new ApplicationShutdownProgressStore();
        using var sut = new ShellShutdownOverlayViewModel(store, new ImmediateUiDispatcher());

        // 直接跳到 Completed（极快的关闭）也不该出现遮罩。
        store.ReportPhase(ApplicationShutdownPhase.Completed);

        Assert.False(sut.IsOverlayVisible);
    }
}
