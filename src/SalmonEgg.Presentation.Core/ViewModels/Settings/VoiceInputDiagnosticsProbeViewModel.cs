using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Resources;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Services.Input;

namespace SalmonEgg.Presentation.ViewModels.Settings;

public sealed partial class VoiceInputDiagnosticsProbeViewModel : ObservableObject, IDisposable
{
    private enum AuthorizationProbeRetryState
    {
        None = 0,
        WaitingForActivation,
        WaitingForProbeReady,
        Starting
    }

    private enum ProbeStatusKind
    {
        Idle,
        Unsupported,
        Busy,
        Authorizing,
        Starting,
        Listening,
        Stopping,
        PartialOnly,
        FinalReceived,
        ReadyWithoutRecognition,
        Failed,
        FailedWithMessage
    }

    private static readonly TimeSpan SignalPollInterval = TimeSpan.FromMilliseconds(100);

    private readonly IVoiceInputService _voiceInputService;
    private readonly IAudioInputSignalDiagnosticsService _signalDiagnosticsService;
    private readonly IApplicationActivationSignalSource _applicationActivationSignalSource;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly IStringLocalizer<CoreStrings> _localizer;
    private readonly ILogger<VoiceInputDiagnosticsProbeViewModel> _logger;
    private readonly IAppLanguageService? _languageService;
    private readonly SemaphoreSlim _signalMonitoringGate = new(1, 1);
    private CancellationTokenSource? _probeCts;
    private CancellationTokenSource? _signalMonitoringCancellationTokenSource;
    private Task? _signalMonitoringTask;
    private int _signalMonitoringActive;
    private string? _activeRequestId;
    private DateTimeOffset? _startRequestedAt;
    private DateTimeOffset? _recognizerReadyAt;
    private DateTimeOffset? _firstPartialAt;
    private DateTimeOffset? _finalResultAt;
    private DateTimeOffset? _stopRequestedAt;
    private DateTimeOffset? _endedAt;
    private DateTimeOffset? _errorAt;
    private string? _errorMessage;
    private string? _capturedText;
    private AudioInputSignalDiagnosticsSnapshot? _signalSnapshot;
    private string? _signalFailureMessage;
    private AuthorizationProbeRetryState _authorizationProbeRetryState;
    private ProbeStatusKind _probeStatusKind;
    private bool _disposed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStartProbe))]
    [NotifyPropertyChangedFor(nameof(CanStopProbe))]
    private bool _isRunning;

    [ObservableProperty]
    private string _probeStatusText;

    [ObservableProperty]
    private string _probeTimelineText;

    [ObservableProperty]
    private string _probeCapturedText;

    [ObservableProperty]
    private string _probeSignalObservationText;

    [ObservableProperty]
    private string _probeSignalTimelineText;

    public VoiceInputDiagnosticsProbeViewModel(
        IVoiceInputService voiceInputService,
        IAudioInputSignalDiagnosticsService signalDiagnosticsService,
        IUiDispatcher uiDispatcher,
        IStringLocalizer<CoreStrings> localizer,
        ILogger<VoiceInputDiagnosticsProbeViewModel> logger,
        IApplicationActivationSignalSource? applicationActivationSignalSource = null,
        IAppLanguageService? languageService = null)
    {
        _voiceInputService = voiceInputService ?? throw new ArgumentNullException(nameof(voiceInputService));
        _signalDiagnosticsService = signalDiagnosticsService ?? throw new ArgumentNullException(nameof(signalDiagnosticsService));
        _applicationActivationSignalSource = applicationActivationSignalSource ?? NoOpApplicationActivationSignalSource.Instance;
        _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _languageService = languageService;
        _probeStatusKind = ProbeStatusKind.Idle;
        _probeStatusText = ResolveProbeStatusText();
        _probeTimelineText = _localizer["VoiceDiagnostics_ProbeTimelinePending"];
        _probeCapturedText = _localizer["VoiceDiagnostics_ProbeNoCapturedText"];
        _probeSignalObservationText = _localizer["VoiceDiagnostics_ProbeSignalPending"];
        _probeSignalTimelineText = _localizer["VoiceDiagnostics_ProbeSignalTimelinePending"];
        _voiceInputService.PartialResultReceived += OnPartialResultReceived;
        _voiceInputService.FinalResultReceived += OnFinalResultReceived;
        _voiceInputService.SessionEnded += OnSessionEnded;
        _voiceInputService.ErrorOccurred += OnErrorOccurred;
        _applicationActivationSignalSource.Activated += OnApplicationActivated;
        if (_languageService is not null)
        {
            _languageService.LanguageChanged += OnLanguageChanged;
        }
    }

    public bool CanStartProbe => !IsRunning;

    public bool CanStopProbe => IsRunning;

    [RelayCommand]
    private Task StartProbeAsync()
        => StartProbeCoreAsync(
            requestAuthorizationHelpOnDenied: true,
            isAuthorizationResumeAttempt: false);

    private async Task StartProbeCoreAsync(
        bool requestAuthorizationHelpOnDenied,
        bool isAuthorizationResumeAttempt)
    {
        if (IsRunning)
        {
            if (isAuthorizationResumeAttempt)
            {
                _authorizationProbeRetryState = AuthorizationProbeRetryState.WaitingForProbeReady;
            }

            return;
        }

        if (!_voiceInputService.IsSupported)
        {
            await RunOnUiAsync(() =>
            {
                SetProbeStatus(ProbeStatusKind.Unsupported);
                ProbeTimelineText = _localizer["VoiceDiagnostics_TimelineUnavailable"];
                SetCapturedText(null);
            }).ConfigureAwait(false);
            return;
        }

        if (_voiceInputService.IsListening)
        {
            await RunOnUiAsync(() =>
            {
                SetProbeStatus(ProbeStatusKind.Busy);
            }).ConfigureAwait(false);
            return;
        }

        if (!isAuthorizationResumeAttempt)
        {
            ClearPendingAuthorizationProbeRetry();
        }

        await RunOnUiAsync(() =>
        {
            ResetProbeState();
            IsRunning = true;
            SetProbeStatus(ProbeStatusKind.Authorizing);
            ProbeTimelineText = _localizer["VoiceDiagnostics_ProbeTimelinePending"];
            SetCapturedText(null);
            ResetSignalProjection();
        }).ConfigureAwait(false);

        TryDisposeProbeCts();
        _probeCts = new CancellationTokenSource();

        try
        {
            var requestId = Guid.NewGuid().ToString("N");
            var languageTag = CultureInfo.CurrentUICulture.Name;
            await RunOnUiAsync(() =>
            {
                _activeRequestId = requestId;
                _startRequestedAt = DateTimeOffset.Now;
                SetProbeStatus(ProbeStatusKind.Starting);
            }).ConfigureAwait(false);
            await StartSignalMonitoringAsync(_probeCts.Token).ConfigureAwait(false);
            _logger.LogInformation(
                "Voice diagnostics probe start requested. RequestId={RequestId} LanguageTag={LanguageTag}",
                requestId,
                languageTag);

            await _voiceInputService.StartAsync(
                new VoiceInputSessionOptions(
                    requestId,
                    languageTag,
                    EnablePartialResults: true,
                    PreferOffline: false),
                _probeCts.Token).ConfigureAwait(false);
            ClearPendingAuthorizationProbeRetry();

            await _uiDispatcher.EnqueueAsync(() =>
            {
                if (!string.Equals(_activeRequestId, requestId, StringComparison.Ordinal))
                {
                    return;
                }

                _recognizerReadyAt = DateTimeOffset.Now;
                SetProbeStatus(ProbeStatusKind.Listening);
                ProbeTimelineText = BuildTimelineText();
            }).ConfigureAwait(false);

            _logger.LogInformation(
                "Voice diagnostics probe recognizer ready. RequestId={RequestId} LanguageTag={LanguageTag}",
                requestId,
                languageTag);
        }
        catch (OperationCanceledException)
        {
            // Stop/teardown path owns the visible terminal state.
        }
        catch (Exception ex)
        {
            if (ex is VoiceInputStartFailureException startFailure && startFailure.RequiresAuthorization)
            {
                if (requestAuthorizationHelpOnDenied)
                {
                    await RequestProbeAuthorizationHelpAndArmRetryAsync(_probeCts.Token).ConfigureAwait(false);
                }
                else
                {
                    ClearPendingAuthorizationProbeRetry();
                }
            }
            else
            {
                ClearPendingAuthorizationProbeRetry();
            }

            _logger.LogWarning(ex, "Voice diagnostics probe failed before completion. RequestId={RequestId}", _activeRequestId);
            await RunOnUiAsync(() =>
            {
                _errorAt = DateTimeOffset.Now;
                _errorMessage = VoiceInputErrorMessageSanitizer.Normalize(ex.Message, "Voice input failed.");
                SetProbeStatus(ProbeStatusKind.FailedWithMessage);
                ProbeTimelineText = BuildTimelineText();
                IsRunning = false;
                _activeRequestId = null;
                TryDisposeProbeCts();
                TryResumeAuthorizationProbeIfReady();
            }).ConfigureAwait(false);
            await StopSignalMonitoringAsync().ConfigureAwait(false);
            TryResumeAuthorizationProbeIfReady();
        }
    }

    private async Task RequestProbeAuthorizationHelpAndArmRetryAsync(CancellationToken cancellationToken)
    {
        ArmPendingAuthorizationProbeRetry();

        var opened = false;
        try
        {
            opened = await _voiceInputService.TryRequestAuthorizationHelpAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            opened = false;
        }

        if (!opened)
        {
            ClearPendingAuthorizationProbeRetry();
        }
    }

    private void ArmPendingAuthorizationProbeRetry()
        => _authorizationProbeRetryState = AuthorizationProbeRetryState.WaitingForActivation;

    private void ClearPendingAuthorizationProbeRetry()
        => _authorizationProbeRetryState = AuthorizationProbeRetryState.None;

    private void OnApplicationActivated(object? sender, EventArgs e)
    {
        if (_authorizationProbeRetryState != AuthorizationProbeRetryState.WaitingForActivation)
        {
            return;
        }

        _authorizationProbeRetryState = AuthorizationProbeRetryState.WaitingForProbeReady;
        TryResumeAuthorizationProbeIfReady();
    }

    private void TryResumeAuthorizationProbeIfReady()
    {
        if (_authorizationProbeRetryState != AuthorizationProbeRetryState.WaitingForProbeReady)
        {
            return;
        }

        if (IsRunning)
        {
            return;
        }

        _authorizationProbeRetryState = AuthorizationProbeRetryState.Starting;
        _ = ScheduleProbeAuthorizationRetryStartAsync();
    }

    private async Task ScheduleProbeAuthorizationRetryStartAsync()
    {
        await Task.Yield();
        await _uiDispatcher.EnqueueAsync(() => StartProbeCoreAsync(
            requestAuthorizationHelpOnDenied: false,
            isAuthorizationResumeAttempt: true)).ConfigureAwait(false);
    }

    [RelayCommand]
    private async Task StopProbeAsync()
    {
        string? requestId = null;
        await RunOnUiAsync(() =>
        {
            if (!IsRunning || string.IsNullOrWhiteSpace(_activeRequestId))
            {
                return;
            }

            requestId = _activeRequestId;
            _stopRequestedAt = DateTimeOffset.Now;
            SetProbeStatus(ProbeStatusKind.Stopping);
        }).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(requestId))
        {
            return;
        }

        _logger.LogInformation("Voice diagnostics probe stop requested. RequestId={RequestId}", requestId);

        try
        {
            await _voiceInputService.StopAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Voice diagnostics probe stop failed. RequestId={RequestId}", requestId);
            await RunOnUiAsync(() =>
            {
                _errorAt = DateTimeOffset.Now;
                _errorMessage = VoiceInputErrorMessageSanitizer.Normalize(ex.Message, "Failed to stop voice input.");
                SetProbeStatus(ProbeStatusKind.FailedWithMessage);
                ProbeTimelineText = BuildTimelineText();
                IsRunning = false;
                _activeRequestId = null;
                TryDisposeProbeCts();
            }).ConfigureAwait(false);
        }
        finally
        {
            await StopSignalMonitoringAsync(logSummary: true, requestId: requestId).ConfigureAwait(false);
        }
    }

    public async Task HandlePageUnloadedAsync()
    {
        ClearPendingAuthorizationProbeRetry();
        if (IsRunning)
        {
            await StopProbeAsync().ConfigureAwait(false);
        }
        else
        {
            await StopSignalMonitoringAsync().ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _voiceInputService.PartialResultReceived -= OnPartialResultReceived;
        _voiceInputService.FinalResultReceived -= OnFinalResultReceived;
        _voiceInputService.SessionEnded -= OnSessionEnded;
        _voiceInputService.ErrorOccurred -= OnErrorOccurred;
        _applicationActivationSignalSource.Activated -= OnApplicationActivated;
        if (_languageService is not null)
        {
            _languageService.LanguageChanged -= OnLanguageChanged;
        }
    }

    private void OnPartialResultReceived(object? sender, VoiceInputPartialResult result)
        => _ = _uiDispatcher.EnqueueAsync(() =>
        {
            if (!IsCurrentRequest(result.RequestId))
            {
                return;
            }

            _firstPartialAt ??= DateTimeOffset.Now;
            SetCapturedText(result.Text);
            SetProbeStatus(ProbeStatusKind.PartialOnly);
            ProbeTimelineText = BuildTimelineText();
        });

    private void OnFinalResultReceived(object? sender, VoiceInputFinalResult result)
        => _ = _uiDispatcher.EnqueueAsync(() =>
        {
            if (!IsCurrentRequest(result.RequestId))
            {
                return;
            }

            _finalResultAt = DateTimeOffset.Now;
            SetCapturedText(result.Text);
            SetProbeStatus(ProbeStatusKind.FinalReceived);
            ProbeTimelineText = BuildTimelineText();
        });

    private void OnSessionEnded(object? sender, VoiceInputSessionEndedResult result)
        => _ = CompleteSessionEndedAsync(result);

    private void OnErrorOccurred(object? sender, VoiceInputErrorResult result)
        => _ = CompleteSessionErrorAsync(result);

    private async Task CompleteSessionEndedAsync(VoiceInputSessionEndedResult result)
    {
        var shouldStopSignalMonitoring = false;
        await _uiDispatcher.EnqueueAsync(() =>
        {
            if (!IsCurrentRequest(result.RequestId))
            {
                return;
            }

            _endedAt = DateTimeOffset.Now;
            SetProbeStatus(ResolveCompletedStatusKind());
            ProbeTimelineText = BuildTimelineText();
            IsRunning = false;
            _activeRequestId = null;
            TryDisposeProbeCts();
            shouldStopSignalMonitoring = _stopRequestedAt is null;
        }).ConfigureAwait(false);

        if (shouldStopSignalMonitoring)
        {
            await StopSignalMonitoringAsync(logSummary: true, requestId: result.RequestId).ConfigureAwait(false);
        }
    }

    private async Task CompleteSessionErrorAsync(VoiceInputErrorResult result)
    {
        var shouldStopSignalMonitoring = false;
        await _uiDispatcher.EnqueueAsync(() =>
        {
            if (!IsCurrentRequest(result.RequestId))
            {
                return;
            }

            _errorAt = DateTimeOffset.Now;
            _errorMessage = result.Message;
            SetProbeStatus(ProbeStatusKind.FailedWithMessage);
            ProbeTimelineText = BuildTimelineText();
            IsRunning = false;
            _activeRequestId = null;
            TryDisposeProbeCts();
            shouldStopSignalMonitoring = _stopRequestedAt is null;
        }).ConfigureAwait(false);

        if (shouldStopSignalMonitoring)
        {
            await StopSignalMonitoringAsync(logSummary: true, requestId: result.RequestId).ConfigureAwait(false);
        }
    }

    private ProbeStatusKind ResolveCompletedStatusKind()
    {
        if (_errorAt is not null)
        {
            return string.IsNullOrWhiteSpace(_errorMessage)
                ? ProbeStatusKind.Failed
                : ProbeStatusKind.FailedWithMessage;
        }

        if (_finalResultAt is not null)
        {
            return ProbeStatusKind.FinalReceived;
        }

        if (_firstPartialAt is not null)
        {
            return ProbeStatusKind.PartialOnly;
        }

        if (_recognizerReadyAt is not null)
        {
            return ProbeStatusKind.ReadyWithoutRecognition;
        }

        return ProbeStatusKind.Idle;
    }

    private string BuildTimelineText()
    {
        if (_startRequestedAt is not null && _recognizerReadyAt is not null)
        {
            var startToReady = _recognizerReadyAt.Value - _startRequestedAt.Value;
            if (_finalResultAt is not null)
            {
                var readyToFinal = _finalResultAt.Value - _recognizerReadyAt.Value;
                return string.Format(
                    CultureInfo.InvariantCulture,
                    _localizer["VoiceDiagnostics_TimelineFinalFormat"],
                    startToReady.TotalSeconds,
                    readyToFinal.TotalSeconds);
            }

            if (_firstPartialAt is not null)
            {
                var readyToPartial = _firstPartialAt.Value - _recognizerReadyAt.Value;
                return string.Format(
                    CultureInfo.InvariantCulture,
                    _localizer["VoiceDiagnostics_TimelinePartialFormat"],
                    startToReady.TotalSeconds,
                    readyToPartial.TotalSeconds);
            }

            var endAnchor = _endedAt ?? _stopRequestedAt;
            if (endAnchor is not null)
            {
                var readyToEnd = endAnchor.Value - _recognizerReadyAt.Value;
                return string.Format(
                    CultureInfo.InvariantCulture,
                    _localizer["VoiceDiagnostics_TimelineReadyWithoutRecognitionFormat"],
                    startToReady.TotalSeconds,
                    readyToEnd.TotalSeconds);
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                _localizer["VoiceDiagnostics_TimelineStartToReadyFormat"],
                startToReady.TotalSeconds);
        }

        return _localizer["VoiceDiagnostics_ProbeTimelinePending"];
    }

    private bool IsCurrentRequest(string requestId)
        => !string.IsNullOrWhiteSpace(requestId)
            && string.Equals(_activeRequestId, requestId, StringComparison.Ordinal);

    private void ResetProbeState()
    {
        _activeRequestId = null;
        _startRequestedAt = null;
        _recognizerReadyAt = null;
        _firstPartialAt = null;
        _finalResultAt = null;
        _stopRequestedAt = null;
        _endedAt = null;
        _errorAt = null;
        _errorMessage = null;
        _capturedText = null;
        _signalSnapshot = null;
        _signalFailureMessage = null;
    }

    private void TryDisposeProbeCts()
    {
        try
        {
            _probeCts?.Dispose();
        }
        catch
        {
        }

        _probeCts = null;
    }

    private async Task StartSignalMonitoringAsync(CancellationToken cancellationToken)
    {
        await StopSignalMonitoringAsync().ConfigureAwait(false);

        AudioInputSignalDiagnosticsSnapshot snapshot;
        try
        {
            await _signalMonitoringGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            await _signalDiagnosticsService.StartMonitoringAsync(cancellationToken).ConfigureAwait(false);
            snapshot = _signalDiagnosticsService.GetCurrentSnapshot();

            if (snapshot.IsSupported)
            {
                Interlocked.Exchange(ref _signalMonitoringActive, 1);
                var pollingCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _signalMonitoringCancellationTokenSource = pollingCancellationTokenSource;
                _signalMonitoringTask = Task.Run(() => ObserveSignalMonitoringAsync(pollingCancellationTokenSource));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Voice diagnostics signal monitoring failed to start.");
            await RunOnUiAsync(() =>
            {
                SetSignalFailure(VoiceInputErrorMessageSanitizer.Normalize(
                    ex.Message,
                    "Signal monitoring failed."));
            }).ConfigureAwait(false);
            return;
        }
        finally
        {
            _signalMonitoringGate.Release();
        }

        await RunOnUiAsync(() => ApplySignalSnapshot(snapshot)).ConfigureAwait(false);
    }

    private async Task StopSignalMonitoringAsync(bool logSummary = false, string? requestId = null)
    {
        await _signalMonitoringGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var cancellationTokenSource = _signalMonitoringCancellationTokenSource;
            var monitoringTask = _signalMonitoringTask;
            _signalMonitoringCancellationTokenSource = null;
            _signalMonitoringTask = null;
            var shouldStopService = cancellationTokenSource is not null
                || monitoringTask is not null
                || Interlocked.Exchange(ref _signalMonitoringActive, 0) == 1;

            try
            {
                cancellationTokenSource?.Cancel();
            }
            catch
            {
            }

            try
            {
                if (monitoringTask is not null)
                {
                    await monitoringTask.ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                cancellationTokenSource?.Dispose();
            }

            if (!shouldStopService)
            {
                return;
            }

            try
            {
                await _signalDiagnosticsService.StopMonitoringAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Voice diagnostics signal monitoring failed to stop.");
            }

            if (logSummary)
            {
                LogSignalMonitoringSummary(requestId, _signalDiagnosticsService.GetCurrentSnapshot());
            }
        }
        finally
        {
            _signalMonitoringGate.Release();
        }
    }

    private async Task ObserveSignalMonitoringAsync(CancellationTokenSource cancellationTokenSource)
    {
        try
        {
            while (!cancellationTokenSource.IsCancellationRequested)
            {
                var snapshot = _signalDiagnosticsService.GetCurrentSnapshot();
                await _uiDispatcher.EnqueueAsync(() => ApplySignalSnapshot(snapshot)).ConfigureAwait(false);
                await Task.Delay(SignalPollInterval, cancellationTokenSource.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationTokenSource.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Voice diagnostics signal monitoring polling failed.");
            await _uiDispatcher.EnqueueAsync(() =>
            {
                SetSignalFailure(VoiceInputErrorMessageSanitizer.Normalize(
                    ex.Message,
                    "Signal monitoring failed."));
            }).ConfigureAwait(false);
        }
    }

    private void ApplySignalSnapshot(AudioInputSignalDiagnosticsSnapshot snapshot)
    {
        _signalSnapshot = snapshot;
        _signalFailureMessage = null;
        if (!snapshot.IsSupported)
        {
            ProbeSignalObservationText = _localizer["VoiceDiagnostics_ProbeSignalUnsupported"];
            ProbeSignalTimelineText = _localizer["VoiceDiagnostics_ProbeSignalTimelinePending"];
            return;
        }

        if (!string.IsNullOrWhiteSpace(snapshot.FailureMessage))
        {
            ProbeSignalObservationText = string.Format(
                CultureInfo.InvariantCulture,
                _localizer["VoiceDiagnostics_ProbeSignalFailureFormat"],
                snapshot.FailureMessage);
            ProbeSignalTimelineText = _localizer["VoiceDiagnostics_ProbeSignalTimelinePending"];
            return;
        }

        if (snapshot.ObservedSampleCount <= 0)
        {
            ProbeSignalObservationText = _localizer["VoiceDiagnostics_ProbeSignalNoSamples"];
            ProbeSignalTimelineText = _localizer["VoiceDiagnostics_ProbeSignalTimelinePending"];
            return;
        }

        if (snapshot.ObservedNonSilentSampleCount <= 0)
        {
            ProbeSignalObservationText = string.Format(
                CultureInfo.InvariantCulture,
                _localizer["VoiceDiagnostics_ProbeSignalSilentFormat"],
                snapshot.ObservedSampleCount,
                snapshot.MaxPeakLevel);
            ProbeSignalTimelineText = _localizer["VoiceDiagnostics_ProbeSignalTimelinePending"];
            return;
        }

        ProbeSignalObservationText = string.Format(
            CultureInfo.InvariantCulture,
            _localizer["VoiceDiagnostics_ProbeSignalDetectedFormat"],
            snapshot.ObservedSampleCount,
            snapshot.ObservedNonSilentSampleCount,
            snapshot.MaxPeakLevel);

        if (_startRequestedAt is not null
            && snapshot.FirstNonSilentSampleObservedAt is not null
            && snapshot.LastNonSilentSampleObservedAt is not null)
        {
            ProbeSignalTimelineText = string.Format(
                CultureInfo.InvariantCulture,
                _localizer["VoiceDiagnostics_ProbeSignalTimelineFormat"],
                (snapshot.FirstNonSilentSampleObservedAt.Value - _startRequestedAt.Value).TotalSeconds,
                (snapshot.LastNonSilentSampleObservedAt.Value - _startRequestedAt.Value).TotalSeconds);
            return;
        }

        ProbeSignalTimelineText = _localizer["VoiceDiagnostics_ProbeSignalTimelinePending"];
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
        => _ = RunOnUiAsync(ReprojectLocalizedState);

    private void ReprojectLocalizedState()
    {
        ProbeStatusText = ResolveProbeStatusText();
        ProbeTimelineText = _probeStatusKind == ProbeStatusKind.Unsupported
            ? _localizer["VoiceDiagnostics_TimelineUnavailable"]
            : BuildTimelineText();
        ProbeCapturedText = _capturedText ?? _localizer["VoiceDiagnostics_ProbeNoCapturedText"];
        ProjectSignalState();
    }

    private void SetProbeStatus(ProbeStatusKind statusKind)
    {
        _probeStatusKind = statusKind;
        ProbeStatusText = ResolveProbeStatusText();
    }

    private string ResolveProbeStatusText()
        => _probeStatusKind switch
        {
            ProbeStatusKind.Unsupported => _localizer["VoiceDiagnostics_Unsupported"],
            ProbeStatusKind.Busy => _localizer["VoiceDiagnostics_ProbeBusy"],
            ProbeStatusKind.Authorizing => _localizer["VoiceDiagnostics_ProbeAuthorizing"],
            ProbeStatusKind.Starting => _localizer["VoiceDiagnostics_ProbeStarting"],
            ProbeStatusKind.Listening => _localizer["VoiceDiagnostics_ProbeListening"],
            ProbeStatusKind.Stopping => _localizer["VoiceDiagnostics_ProbeStopping"],
            ProbeStatusKind.PartialOnly => _localizer["VoiceDiagnostics_SessionPartialOnly"],
            ProbeStatusKind.FinalReceived => _localizer["VoiceDiagnostics_SessionFinalReceived"],
            ProbeStatusKind.ReadyWithoutRecognition => _localizer["VoiceDiagnostics_SessionReadyWithoutRecognition"],
            ProbeStatusKind.Failed => _localizer["VoiceDiagnostics_SessionFailed"],
            ProbeStatusKind.FailedWithMessage => string.Format(
                CultureInfo.InvariantCulture,
                _localizer["VoiceDiagnostics_SessionFailedWithMessage"],
                _errorMessage),
            _ => _localizer["VoiceDiagnostics_ProbeIdle"]
        };

    private void SetCapturedText(string? capturedText)
    {
        _capturedText = capturedText;
        ProbeCapturedText = capturedText ?? _localizer["VoiceDiagnostics_ProbeNoCapturedText"];
    }

    private void ResetSignalProjection()
    {
        _signalSnapshot = null;
        _signalFailureMessage = null;
        ProjectSignalState();
    }

    private void SetSignalFailure(string failureMessage)
    {
        _signalSnapshot = null;
        _signalFailureMessage = failureMessage;
        ProjectSignalState();
    }

    private void ProjectSignalState()
    {
        if (!string.IsNullOrWhiteSpace(_signalFailureMessage))
        {
            ProbeSignalObservationText = string.Format(
                CultureInfo.InvariantCulture,
                _localizer["VoiceDiagnostics_ProbeSignalFailureFormat"],
                _signalFailureMessage);
            ProbeSignalTimelineText = _localizer["VoiceDiagnostics_ProbeSignalTimelinePending"];
            return;
        }

        if (_signalSnapshot is not null)
        {
            ApplySignalSnapshot(_signalSnapshot);
            return;
        }

        ProbeSignalObservationText = _localizer["VoiceDiagnostics_ProbeSignalPending"];
        ProbeSignalTimelineText = _localizer["VoiceDiagnostics_ProbeSignalTimelinePending"];
    }

    private void LogSignalMonitoringSummary(string? requestId, AudioInputSignalDiagnosticsSnapshot snapshot)
    {
        _logger.LogInformation(
            "Voice diagnostics signal summary. RequestId={RequestId} IsSupported={IsSupported} ObservedSampleCount={ObservedSampleCount} ObservedNonSilentSampleCount={ObservedNonSilentSampleCount} MaxPeakLevel={MaxPeakLevel} FailureMessage={FailureMessage}",
            requestId,
            snapshot.IsSupported,
            snapshot.ObservedSampleCount,
            snapshot.ObservedNonSilentSampleCount,
            snapshot.MaxPeakLevel,
            snapshot.FailureMessage);
    }

    private Task RunOnUiAsync(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (_uiDispatcher.HasThreadAccess)
        {
            action();
            return Task.CompletedTask;
        }

        return _uiDispatcher.EnqueueAsync(action);
    }
}
