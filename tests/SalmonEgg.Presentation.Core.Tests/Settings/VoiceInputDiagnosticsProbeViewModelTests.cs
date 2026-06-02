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
        var signalService = new FakeAudioInputSignalDiagnosticsService();
        var viewModel = CreateViewModel(service, signalService: signalService);

        await viewModel.StartProbeCommand.ExecuteAsync(null);
        signalService.SetSnapshot(new AudioInputSignalDiagnosticsSnapshot(
            IsSupported: true,
            IsMonitoring: true,
            ObservedSampleCount: 6,
            ObservedNonSilentSampleCount: 3,
            MaxPeakLevel: 0.42,
            FirstNonSilentSampleObservedAt: DateTimeOffset.Now.AddMilliseconds(-200),
            LastNonSilentSampleObservedAt: DateTimeOffset.Now.AddMilliseconds(-20),
            FailureMessage: null));
        await Task.Delay(150);
        service.RaisePartial("测试");
        service.RaiseFinal("测试语音");
        service.RaiseSessionEnded();

        Assert.False(viewModel.IsRunning);
        Assert.Contains("final", viewModel.ProbeStatusText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("测试语音", viewModel.ProbeCapturedText);
        Assert.DoesNotContain("暂无", viewModel.ProbeTimelineText, StringComparison.Ordinal);
        Assert.Contains("非静音", viewModel.ProbeSignalObservationText);
        Assert.Contains("0.42", viewModel.ProbeSignalObservationText);
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
        var signalService = new FakeAudioInputSignalDiagnosticsService();
        var viewModel = CreateViewModel(service, signalService: signalService);

        await viewModel.StartProbeCommand.ExecuteAsync(null);
        signalService.SetSnapshot(new AudioInputSignalDiagnosticsSnapshot(
            IsSupported: true,
            IsMonitoring: true,
            ObservedSampleCount: 4,
            ObservedNonSilentSampleCount: 0,
            MaxPeakLevel: 0.03,
            FirstNonSilentSampleObservedAt: null,
            LastNonSilentSampleObservedAt: null,
            FailureMessage: null));
        await Task.Delay(150);
        await viewModel.StopProbeCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsRunning);
        Assert.Contains("没有收到任何识别结果", viewModel.ProbeStatusText);
        Assert.Equal("暂无识别文本。", viewModel.ProbeCapturedText);
        Assert.Contains("静音", viewModel.ProbeSignalObservationText);
        Assert.Contains("0.03", viewModel.ProbeSignalObservationText);
    }

    [Fact]
    public async Task StartProbeAsync_StartsAndStopsSignalDiagnosticsMonitoring()
    {
        var service = new FakeVoiceInputService();
        var signalService = new FakeAudioInputSignalDiagnosticsService();
        var viewModel = CreateViewModel(service, signalService: signalService);

        await viewModel.StartProbeCommand.ExecuteAsync(null);
        await viewModel.StopProbeCommand.ExecuteAsync(null);
        await WaitForConditionAsync(() => Task.FromResult(signalService.StopCallCount == 1));

        Assert.Equal(1, signalService.StartCallCount);
        Assert.Equal(1, signalService.StopCallCount);
    }

    [Fact]
    public async Task StopProbeAsync_DoesNotCancelServiceStartTokenBeforeRequestingGracefulStop()
    {
        var service = new FakeVoiceInputService();
        var viewModel = CreateViewModel(service);

        await viewModel.StartProbeCommand.ExecuteAsync(null);
        await viewModel.StopProbeCommand.ExecuteAsync(null);

        Assert.False(service.WasStartCancellationRequestedWhenStopCalled);
    }

    [Fact]
    public async Task StopProbeAsync_LogsSignalSummary()
    {
        var service = new FakeVoiceInputService();
        var signalService = new FakeAudioInputSignalDiagnosticsService();
        var logger = new Mock<ILogger<VoiceInputDiagnosticsProbeViewModel>>();
        var viewModel = CreateViewModel(service, signalService: signalService, logger: logger);

        await viewModel.StartProbeCommand.ExecuteAsync(null);
        signalService.SetSnapshot(new AudioInputSignalDiagnosticsSnapshot(
            IsSupported: true,
            IsMonitoring: true,
            ObservedSampleCount: 9,
            ObservedNonSilentSampleCount: 4,
            MaxPeakLevel: 0.55,
            FirstNonSilentSampleObservedAt: DateTimeOffset.Now.AddMilliseconds(-300),
            LastNonSilentSampleObservedAt: DateTimeOffset.Now.AddMilliseconds(-40),
            FailureMessage: null));

        await viewModel.StopProbeCommand.ExecuteAsync(null);

        logger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) => state.ToString()!.Contains("Voice diagnostics signal summary.", StringComparison.Ordinal)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
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

    [Fact]
    public async Task StartProbeAsync_WhenAuthorizationGrantedAfterReturn_RetriesAutomaticallyOnNextActivation()
    {
        var service = new FakeVoiceInputService
        {
            PermissionResult = new VoiceInputPermissionResult(
                VoiceInputPermissionStatus.Denied,
                "Enable speech access",
                RequiresAuthorization: true)
        };
        var activationSource = new FakeApplicationActivationSignalSource();
        var viewModel = CreateViewModel(service, activationSource: activationSource);

        await viewModel.StartProbeCommand.ExecuteAsync(null);

        Assert.Equal(1, service.AuthorizationHelpRequestCount);
        Assert.False(viewModel.IsRunning);
        Assert.Null(service.LastOptions);

        service.PermissionResult = VoiceInputPermissionResult.Granted();
        activationSource.RaiseActivated();

        await WaitForConditionAsync(() => Task.FromResult(
            service.StartCount == 1
            && viewModel.IsRunning));

        Assert.Equal(2, service.PermissionRequestCount);
        Assert.Equal(1, service.AuthorizationHelpRequestCount);
    }

    [Fact]
    public async Task StartProbeAsync_WhenActivationArrivesBeforeDeniedFlowSettles_RetriesAfterProbeBecomesIdle()
    {
        var activationSource = new FakeApplicationActivationSignalSource();
        var dispatcher = new BufferedActionUiDispatcher();
        var service = new FakeVoiceInputService
        {
            PermissionResult = new VoiceInputPermissionResult(
                VoiceInputPermissionStatus.Denied,
                "Enable speech access",
                RequiresAuthorization: true)
        };
        service.OnAuthorizationHelpRequested = () =>
        {
            dispatcher.BufferNextAction();
        };
        var viewModel = CreateViewModel(service, dispatcher: dispatcher, activationSource: activationSource);

        await viewModel.StartProbeCommand.ExecuteAsync(null);

        Assert.Equal(1, service.AuthorizationHelpRequestCount);
        Assert.True(viewModel.IsRunning);

        service.PermissionResult = VoiceInputPermissionResult.Granted();
        activationSource.RaiseActivated();
        dispatcher.FlushBufferedActions();

        await WaitForConditionAsync(() => Task.FromResult(
            service.StartCount == 1
            && viewModel.IsRunning));

        Assert.Equal(2, service.PermissionRequestCount);
    }

    [Fact]
    public async Task HandlePageUnloadedAsync_ClearsPendingAuthorizationRetry()
    {
        var service = new FakeVoiceInputService
        {
            PermissionResult = new VoiceInputPermissionResult(
                VoiceInputPermissionStatus.Denied,
                "Enable speech access",
                RequiresAuthorization: true)
        };
        var activationSource = new FakeApplicationActivationSignalSource();
        var viewModel = CreateViewModel(service, activationSource: activationSource);

        await viewModel.StartProbeCommand.ExecuteAsync(null);
        await viewModel.HandlePageUnloadedAsync();

        service.PermissionResult = VoiceInputPermissionResult.Granted();
        activationSource.RaiseActivated();
        await Task.Delay(100);

        Assert.Equal(0, service.StartCount);
        Assert.False(viewModel.IsRunning);
    }

    private static VoiceInputDiagnosticsProbeViewModel CreateViewModel(
        FakeVoiceInputService service,
        IUiDispatcher? dispatcher = null,
        FakeAudioInputSignalDiagnosticsService? signalService = null,
        IApplicationActivationSignalSource? activationSource = null,
        Mock<ILogger<VoiceInputDiagnosticsProbeViewModel>>? logger = null)
        => new(
            service,
            signalService ?? new FakeAudioInputSignalDiagnosticsService(),
            dispatcher ?? new TrackingUiDispatcher(),
            new TestCoreStringLocalizer(),
            logger?.Object ?? Mock.Of<ILogger<VoiceInputDiagnosticsProbeViewModel>>(),
            activationSource);

    private static async Task WaitForConditionAsync(Func<Task<bool>> predicate, int timeoutMilliseconds = 2000, int pollDelayMilliseconds = 20)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
        while (DateTime.UtcNow < deadline)
        {
            if (await predicate().ConfigureAwait(false))
            {
                return;
            }

            await Task.Delay(pollDelayMilliseconds).ConfigureAwait(false);
        }

        Assert.True(await predicate().ConfigureAwait(false), "Condition was not satisfied within the allotted time.");
    }

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

    private sealed class BufferedActionUiDispatcher : IUiDispatcher
    {
        private Action? _bufferedAction;
        private bool _bufferNextAction;

        public bool HasThreadAccess => false;

        public void BufferNextAction()
            => _bufferNextAction = true;

        public void FlushBufferedActions()
        {
            var action = _bufferedAction;
            _bufferedAction = null;
            action?.Invoke();
        }

        public void Enqueue(Action action)
        {
            EnqueueAsync(action).GetAwaiter().GetResult();
        }

        public Task EnqueueAsync(Action action)
        {
            if (_bufferNextAction)
            {
                _bufferNextAction = false;
                _bufferedAction = action;
                return Task.CompletedTask;
            }

            action();
            return Task.CompletedTask;
        }

        public Task EnqueueAsync(Func<Task> action)
            => action();
    }

    private sealed class FakeVoiceInputService : IVoiceInputService
    {
        public VoiceInputPermissionResult PermissionResult { get; set; } = VoiceInputPermissionResult.Granted();

        public bool IsListeningOverride { get; set; }

        public bool DelayPermissionAsync { get; set; }

        public bool DelayStartAsync { get; set; }

        public int PermissionRequestCount { get; private set; }

        public int AuthorizationHelpRequestCount { get; private set; }

        public int StartCount { get; private set; }

        public bool WasStartCancellationRequestedWhenStopCalled { get; private set; }

        public Action? OnAuthorizationHelpRequested { get; set; }

        public VoiceInputSessionOptions? LastOptions { get; private set; }

        private CancellationToken LastStartCancellationToken { get; set; }

        public bool IsSupported => true;

        public bool IsListening => IsListeningOverride || LastOptions is not null;

        public event EventHandler<VoiceInputPartialResult>? PartialResultReceived;

        public event EventHandler<VoiceInputFinalResult>? FinalResultReceived;

        public event EventHandler<VoiceInputSessionEndedResult>? SessionEnded;

        public event EventHandler<VoiceInputErrorResult>? ErrorOccurred;

        public Task<VoiceInputPermissionResult> EnsurePermissionAsync(CancellationToken cancellationToken = default)
        {
            PermissionRequestCount++;
            return DelayPermissionAsync
                ? Task.Run(async () =>
                {
                    await Task.Yield();
                    return PermissionResult;
                }, cancellationToken)
                : Task.FromResult(PermissionResult);
        }

        public Task<bool> TryRequestAuthorizationHelpAsync(CancellationToken cancellationToken = default)
        {
            AuthorizationHelpRequestCount++;
            OnAuthorizationHelpRequested?.Invoke();
            return Task.FromResult(true);
        }

        public Task StartAsync(VoiceInputSessionOptions options, CancellationToken cancellationToken = default)
        {
            StartCount++;
            LastOptions = options;
            LastStartCancellationToken = cancellationToken;
            return DelayStartAsync
                ? Task.Run(async () =>
                {
                    await Task.Yield();
                }, cancellationToken)
                : Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            WasStartCancellationRequestedWhenStopCalled = LastStartCancellationToken.IsCancellationRequested;
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

    private sealed class FakeAudioInputSignalDiagnosticsService : IAudioInputSignalDiagnosticsService
    {
        private AudioInputSignalDiagnosticsSnapshot _snapshot = AudioInputSignalDiagnosticsSnapshot.Unsupported;

        public int StartCallCount { get; private set; }

        public int StopCallCount { get; private set; }

        public AudioInputSignalDiagnosticsSnapshot GetCurrentSnapshot() => _snapshot;

        public Task StartMonitoringAsync(CancellationToken cancellationToken = default)
        {
            StartCallCount++;
            _snapshot = _snapshot with
            {
                IsSupported = true,
                IsMonitoring = true,
                FailureMessage = null
            };
            return Task.CompletedTask;
        }

        public Task StopMonitoringAsync(CancellationToken cancellationToken = default)
        {
            StopCallCount++;
            _snapshot = _snapshot with
            {
                IsMonitoring = false
            };
            return Task.CompletedTask;
        }

        public void SetSnapshot(AudioInputSignalDiagnosticsSnapshot snapshot)
        {
            _snapshot = snapshot;
        }
    }

    private sealed class FakeApplicationActivationSignalSource : IApplicationActivationSignalSource
    {
        public event EventHandler? Activated;

        public void RaiseActivated() => Activated?.Invoke(this, EventArgs.Empty);
    }
}
