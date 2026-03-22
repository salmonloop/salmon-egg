using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using SalmonEgg.Domain.Models.Diagnostics;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.ViewModels.Settings;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Settings;

public sealed class LiveLogViewerViewModelTests
{
    [Fact]
    public async Task StartStreamingAsync_SetsExpandedAndStreaming()
    {
        var service = new TestLiveLogStreamService();
        var logger = new Mock<ILogger<LiveLogViewerViewModel>>();
        var viewModel = new LiveLogViewerViewModel(service, "C:/logs", logger.Object);

        await viewModel.StartStreamingAsync();
        await service.Started.Task;

        Assert.True(viewModel.IsExpanded);
        Assert.True(viewModel.IsStreaming);
        Assert.Equal(1, service.StartCallCount);
        Assert.Equal("C:/logs", service.LastLogsDirectoryPath);
    }

    [Fact]
    public async Task StartStreamingAsync_WhenAlreadyStreaming_IsIdempotent()
    {
        var service = new TestLiveLogStreamService();
        var logger = new Mock<ILogger<LiveLogViewerViewModel>>();
        var viewModel = new LiveLogViewerViewModel(service, "C:/logs", logger.Object);

        await viewModel.StartStreamingAsync();
        await service.Started.Task;
        await viewModel.StartStreamingAsync();

        Assert.Equal(1, service.StartCallCount);
    }

    [Fact]
    public async Task StopStreamingAsync_WhenCalledRepeatedly_IsIdempotent()
    {
        var service = new TestLiveLogStreamService();
        var logger = new Mock<ILogger<LiveLogViewerViewModel>>();
        var viewModel = new LiveLogViewerViewModel(service, "C:/logs", logger.Object);

        await viewModel.StartStreamingAsync();
        await service.Started.Task;

        await viewModel.StopStreamingAsync();
        await viewModel.StopStreamingAsync();

        Assert.False(viewModel.IsStreaming);
        Assert.False(viewModel.IsPaused);
    }

    [Fact]
    public async Task ApplyUpdateAsync_WhenFileSwitches_UpdatesCurrentPathAndStatus()
    {
        var service = new TestLiveLogStreamService();
        var logger = new Mock<ILogger<LiveLogViewerViewModel>>();
        var viewModel = new LiveLogViewerViewModel(service, "C:/logs", logger.Object);

        await viewModel.StartStreamingAsync();
        await service.Started.Task;
        await service.EmitAsync(new LiveLogStreamUpdate("C:/logs/app.log", string.Empty, hasFileSwitched: true));

        Assert.Equal("C:/logs/app.log", viewModel.CurrentLogFilePath);
        Assert.Equal("已切换到最新日志文件", viewModel.StatusText);
    }

    [Fact]
    public async Task ApplyUpdateAsync_AppendsTextAndTrimsToConfiguredWindow()
    {
        var service = new TestLiveLogStreamService();
        var logger = new Mock<ILogger<LiveLogViewerViewModel>>();
        var viewModel = new LiveLogViewerViewModel(service, "C:/logs", logger.Object, maxVisibleCharacters: 8);

        await viewModel.StartStreamingAsync();
        await service.Started.Task;
        await service.EmitAsync(new LiveLogStreamUpdate("C:/logs/app.log", "12345", hasFileSwitched: false));
        await service.EmitAsync(new LiveLogStreamUpdate("C:/logs/app.log", "67890", hasFileSwitched: false));

        Assert.Equal("34567890", viewModel.VisibleLogText);
    }

    [Fact]
    public void Constructor_ExposesStartActionBeforeStreamingBegins()
    {
        var service = new TestLiveLogStreamService();
        var logger = new Mock<ILogger<LiveLogViewerViewModel>>();
        var viewModel = new LiveLogViewerViewModel(service, "C:/logs", logger.Object);

        Assert.True(viewModel.CanStartStreaming);
        Assert.False(viewModel.CanPauseStreaming);
        Assert.False(viewModel.CanResumeStreaming);
        Assert.Equal("未启动", viewModel.StatusText);
    }

    [Fact]
    public async Task PauseStreamingAsync_SwitchesToResumeState()
    {
        var service = new TestLiveLogStreamService();
        var logger = new Mock<ILogger<LiveLogViewerViewModel>>();
        var viewModel = new LiveLogViewerViewModel(service, "C:/logs", logger.Object);

        await viewModel.StartStreamingAsync();
        await service.Started.Task;
        await viewModel.PauseStreamingAsync();

        Assert.False(viewModel.CanStartStreaming);
        Assert.False(viewModel.CanPauseStreaming);
        Assert.True(viewModel.CanResumeStreaming);
        Assert.Equal("已暂停", viewModel.StatusText);
    }

    [Fact]
    public void ExpandingPanel_DoesNotStartStreamingUntilExplicitStart()
    {
        var service = new TestLiveLogStreamService();
        var logger = new Mock<ILogger<LiveLogViewerViewModel>>();
        var viewModel = new LiveLogViewerViewModel(service, "C:/logs", logger.Object);

        viewModel.IsExpanded = true;

        Assert.True(viewModel.IsExpanded);
        Assert.False(viewModel.IsStreaming);
        Assert.Equal(0, service.StartCallCount);
        Assert.True(viewModel.CanStartStreaming);
        Assert.Equal("未启动", viewModel.StatusText);
    }

    [Fact]
    public async Task ApplyUpdateAsync_WhenNoLogFileIsAvailable_ShowsMissingStatus()
    {
        var service = new TestLiveLogStreamService();
        var logger = new Mock<ILogger<LiveLogViewerViewModel>>();
        var viewModel = new LiveLogViewerViewModel(service, "C:/logs", logger.Object);

        await viewModel.StartStreamingAsync();
        await service.Started.Task;
        await service.EmitAsync(new LiveLogStreamUpdate(null, string.Empty, hasFileSwitched: true));

        Assert.Null(viewModel.CurrentLogFilePath);
        Assert.Equal("未找到可用日志文件", viewModel.StatusText);
    }

    [Fact]
    public async Task StartStreamingAsync_CapturesCurrentSynchronizationContextForSubsequentUpdates()
    {
        var originalContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);

        try
        {
            var service = new TestLiveLogStreamService();
            var logger = new Mock<ILogger<LiveLogViewerViewModel>>();
            var viewModel = new LiveLogViewerViewModel(service, "C:/logs", logger.Object);
            var syncContext = new QueueingSynchronizationContext();

            SynchronizationContext.SetSynchronizationContext(syncContext);
            await viewModel.StartStreamingAsync();
            await service.Started.Task;
            SynchronizationContext.SetSynchronizationContext(originalContext);

            var emitTask = Task.Run(() =>
                service.EmitAsync(new LiveLogStreamUpdate("C:/logs/app.log", "tick\n", hasFileSwitched: false)));

            await Task.Delay(50);

            Assert.False(emitTask.IsCompleted);
            Assert.Equal(string.Empty, viewModel.VisibleLogText);

            syncContext.DrainPostedCallbacks();
            await emitTask;

            Assert.Equal("tick\n", viewModel.VisibleLogText);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    private sealed class TestLiveLogStreamService : ILiveLogStreamService
    {
        private Func<LiveLogStreamUpdate, Task>? _onUpdate;

        public int StartCallCount { get; private set; }

        public string? LastLogsDirectoryPath { get; private set; }

        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task StartAsync(
            string logsDirectoryPath,
            Func<LiveLogStreamUpdate, Task> onUpdate,
            CancellationToken cancellationToken)
        {
            StartCallCount++;
            LastLogsDirectoryPath = logsDirectoryPath;
            _onUpdate = onUpdate;
            Started.TrySetResult(true);
            return WaitForCancellationAsync(cancellationToken);
        }

        public Task EmitAsync(LiveLogStreamUpdate update)
        {
            return _onUpdate is null
                ? Task.CompletedTask
                : _onUpdate(update);
        }

        private static Task WaitForCancellationAsync(CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
            return completion.Task;
        }
    }

    private sealed class QueueingSynchronizationContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _callbacks = new();

        public override void Post(SendOrPostCallback d, object? state)
        {
            _callbacks.Enqueue((d, state));
        }

        public void DrainPostedCallbacks()
        {
            while (_callbacks.Count > 0)
            {
                var (callback, state) = _callbacks.Dequeue();
                callback(state);
            }
        }
    }
}
