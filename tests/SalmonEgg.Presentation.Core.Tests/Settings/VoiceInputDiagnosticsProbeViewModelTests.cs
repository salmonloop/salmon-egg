using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Services.Input;
using SalmonEgg.Presentation.Core.Tests.Localization;
using SalmonEgg.Presentation.Core.Tests.Threading;
using SalmonEgg.Presentation.ViewModels.Settings;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Settings;

public sealed class VoiceInputDiagnosticsProbeViewModelTests
{
    [Fact]
    public async Task StartProbeAsync_WhenFinalResultReceived_CapturesRecognizedText()
    {
        var service = new FakeVoiceInputService();
        var viewModel = CreateViewModel(service);

        await viewModel.StartProbeCommand.ExecuteAsync(null);
        service.RaisePartial("测试");
        service.RaiseFinal("测试语音");
        service.RaiseSessionEnded();

        Assert.False(viewModel.IsRunning);
        Assert.Contains("final", viewModel.ProbeStatusText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("测试语音", viewModel.ProbeCapturedText);
        Assert.DoesNotContain("暂无", viewModel.ProbeTimelineText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartProbeAsync_MarshalsBindableStateChangesThroughUiDispatcher()
    {
        var service = new FakeVoiceInputService
        {
            DelayPermissionAsync = true,
            DelayStartAsync = true
        };
        var dispatcher = new TrackingUiDispatcher();
        var viewModel = CreateViewModel(service, dispatcher);
        var startStatusRaisedOnUi = false;
        var listeningStatusRaisedOnUi = false;

        viewModel.PropertyChanged += (_, args) =>
        {
            if (!string.Equals(args.PropertyName, nameof(VoiceInputDiagnosticsProbeViewModel.ProbeStatusText), StringComparison.Ordinal))
            {
                return;
            }

            if (string.Equals(viewModel.ProbeStatusText, "正在启动独立诊断...", StringComparison.Ordinal))
            {
                startStatusRaisedOnUi = dispatcher.IsExecutingCallback;
            }

            if (string.Equals(viewModel.ProbeStatusText, "诊断会话已 ready，正在监听语音。", StringComparison.Ordinal))
            {
                listeningStatusRaisedOnUi = dispatcher.IsExecutingCallback;
            }
        };

        await viewModel.StartProbeCommand.ExecuteAsync(null);

        Assert.True(startStatusRaisedOnUi);
        Assert.True(listeningStatusRaisedOnUi);
    }

    [Fact]
    public async Task StopProbeAsync_FromBackgroundThread_MarshalsBindableStateChangesThroughUiDispatcher()
    {
        var service = new FakeVoiceInputService();
        var dispatcher = new TrackingUiDispatcher();
        var viewModel = CreateViewModel(service, dispatcher);
        var stoppingStatusRaisedOnUi = false;

        viewModel.PropertyChanged += (_, args) =>
        {
            if (!string.Equals(args.PropertyName, nameof(VoiceInputDiagnosticsProbeViewModel.ProbeStatusText), StringComparison.Ordinal))
            {
                return;
            }

            if (string.Equals(viewModel.ProbeStatusText, "正在停止独立诊断...", StringComparison.Ordinal))
            {
                stoppingStatusRaisedOnUi = dispatcher.IsExecutingCallback;
            }
        };

        await viewModel.StartProbeCommand.ExecuteAsync(null);
        await Task.Run(() => viewModel.StopProbeCommand.ExecuteAsync(null));

        Assert.True(stoppingStatusRaisedOnUi);
    }

    [Fact]
    public async Task StopProbeAsync_WhenNoRecognitionResults_ShowsReadyWithoutRecognition()
    {
        var service = new FakeVoiceInputService();
        var viewModel = CreateViewModel(service);

        await viewModel.StartProbeCommand.ExecuteAsync(null);
        await viewModel.StopProbeCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsRunning);
        Assert.Contains("没有收到任何识别结果", viewModel.ProbeStatusText);
        Assert.Equal("暂无识别文本。", viewModel.ProbeCapturedText);
    }

    [Fact]
    public async Task StartProbeAsync_WhenVoiceServiceAlreadyListening_ShowsBusyStatus()
    {
        var service = new FakeVoiceInputService
        {
            IsListeningOverride = true
        };
        var viewModel = CreateViewModel(service);

        await viewModel.StartProbeCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsRunning);
        Assert.Contains("占用", viewModel.ProbeStatusText);
    }

    [Fact]
    public async Task StartProbeAsync_WhenErrorOccurs_ShowsFailureState()
    {
        var service = new FakeVoiceInputService();
        var viewModel = CreateViewModel(service);

        await viewModel.StartProbeCommand.ExecuteAsync(null);
        service.RaiseError("probe error");

        Assert.False(viewModel.IsRunning);
        Assert.Contains("失败", viewModel.ProbeStatusText);
        Assert.Contains("probe error", viewModel.ProbeStatusText);
    }

    private static VoiceInputDiagnosticsProbeViewModel CreateViewModel(
        FakeVoiceInputService service,
        TrackingUiDispatcher? dispatcher = null)
        => new(
            service,
            dispatcher ?? new TrackingUiDispatcher(),
            new TestCoreStringLocalizer(),
            Mock.Of<ILogger<VoiceInputDiagnosticsProbeViewModel>>());

    private sealed class TrackingUiDispatcher : IUiDispatcher
    {
        public bool IsExecutingCallback { get; private set; }

        public bool HasThreadAccess => false;

        public void Enqueue(Action action)
        {
            IsExecutingCallback = true;
            try
            {
                action();
            }
            finally
            {
                IsExecutingCallback = false;
            }
        }

        public Task EnqueueAsync(Action action)
        {
            IsExecutingCallback = true;
            try
            {
                action();
            }
            finally
            {
                IsExecutingCallback = false;
            }

            return Task.CompletedTask;
        }

        public Task EnqueueAsync(Func<Task> action)
        {
            IsExecutingCallback = true;
            try
            {
                return AwaitAndResetAsync(action);
            }
            catch
            {
                IsExecutingCallback = false;
                throw;
            }
        }

        private async Task AwaitAndResetAsync(Func<Task> action)
        {
            try
            {
                await action();
            }
            finally
            {
                IsExecutingCallback = false;
            }
        }
    }

    private sealed class FakeVoiceInputService : IVoiceInputService
    {
        public VoiceInputPermissionResult PermissionResult { get; set; } = VoiceInputPermissionResult.Granted();

        public bool IsListeningOverride { get; set; }

        public bool DelayPermissionAsync { get; set; }

        public bool DelayStartAsync { get; set; }

        public VoiceInputSessionOptions? LastOptions { get; private set; }

        public bool IsSupported => true;

        public bool IsListening => IsListeningOverride || LastOptions is not null;

        public event EventHandler<VoiceInputPartialResult>? PartialResultReceived;

        public event EventHandler<VoiceInputFinalResult>? FinalResultReceived;

        public event EventHandler<VoiceInputSessionEndedResult>? SessionEnded;

        public event EventHandler<VoiceInputErrorResult>? ErrorOccurred;

        public Task<VoiceInputPermissionResult> EnsurePermissionAsync(CancellationToken cancellationToken = default)
            => DelayPermissionAsync
                ? Task.Run(async () =>
                {
                    await Task.Yield();
                    return PermissionResult;
                }, cancellationToken)
                : Task.FromResult(PermissionResult);

        public Task<bool> TryRequestAuthorizationHelpAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task StartAsync(VoiceInputSessionOptions options, CancellationToken cancellationToken = default)
        {
            LastOptions = options;
            return DelayStartAsync
                ? Task.Run(async () =>
                {
                    await Task.Yield();
                }, cancellationToken)
                : Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            RaiseSessionEnded();
            return Task.CompletedTask;
        }

        public void RaiseFinal(string text)
        {
            if (LastOptions is null)
            {
                return;
            }

            FinalResultReceived?.Invoke(this, new VoiceInputFinalResult(LastOptions.Value.RequestId, text));
        }

        public void RaisePartial(string text)
        {
            if (LastOptions is null)
            {
                return;
            }

            PartialResultReceived?.Invoke(this, new VoiceInputPartialResult(LastOptions.Value.RequestId, text));
        }

        public void RaiseError(string message)
        {
            if (LastOptions is null)
            {
                return;
            }

            var requestId = LastOptions.Value.RequestId;
            LastOptions = null;
            ErrorOccurred?.Invoke(this, new VoiceInputErrorResult(requestId, message));
        }

        public void RaiseSessionEnded()
        {
            if (LastOptions is null)
            {
                return;
            }

            var requestId = LastOptions.Value.RequestId;
            LastOptions = null;
            SessionEnded?.Invoke(this, new VoiceInputSessionEndedResult(requestId));
        }

        public void Dispose()
        {
        }
    }
}
