using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using SalmonEgg.Domain.Models.Diagnostics;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Tests.Threading;
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
        var viewModel = new LiveLogViewerViewModel(service, "C:/logs", logger.Object, new ImmediateUiDispatcher());

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
        var viewModel = new LiveLogViewerViewModel(service, "C:/logs", logger.Object, new ImmediateUiDispatcher());

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
        var viewModel = new LiveLogViewerViewModel(service, "C:/logs", logger.Object, new ImmediateUiDispatcher());

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
        var viewModel = new LiveLogViewerViewModel(service, "C:/logs", logger.Object, new ImmediateUiDispatcher());

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
        var viewModel = new LiveLogViewerViewModel(service, "C:/logs", logger.Object, new ImmediateUiDispatcher(), maxVisibleCharacters: 8);

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
        var viewModel = new LiveLogViewerViewModel(service, "C:/logs", logger.Object, new ImmediateUiDispatcher());

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
        var viewModel = new LiveLogViewerViewModel(service, "C:/logs", logger.Object, new ImmediateUiDispatcher());

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
        var viewModel = new LiveLogViewerViewModel(service, "C:/logs", logger.Object, new ImmediateUiDispatcher());

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
        var viewModel = new LiveLogViewerViewModel(service, "C:/logs", logger.Object, new ImmediateUiDispatcher());

        await viewModel.StartStreamingAsync();
        await service.Started.Task;
        await service.EmitAsync(new LiveLogStreamUpdate(null, string.Empty, hasFileSwitched: true));

        Assert.Null(viewModel.CurrentLogFilePath);
        Assert.Equal("未找到可用日志文件", viewModel.StatusText);
    }

    [Fact]
    public async Task StartStreamingAsync_UsesInjectedDispatcherForSubsequentUpdates()
    {
        var service = new TestLiveLogStreamService();
        var logger = new Mock<ILogger<LiveLogViewerViewModel>>();
        var dispatcher = new QueueingUiDispatcher();
        var viewModel = new LiveLogViewerViewModel(service, "C:/logs", logger.Object, dispatcher);

        await viewModel.StartStreamingAsync();
        await service.Started.Task;
        var emitTask = service.EmitAsync(new LiveLogStreamUpdate("C:/logs/app.log", "tick\n", hasFileSwitched: false));

        Assert.Equal(string.Empty, viewModel.VisibleLogText);

        dispatcher.RunAll();
        await emitTask;

        Assert.Equal("tick\n", viewModel.VisibleLogText);
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
}
