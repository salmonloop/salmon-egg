using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SalmonEgg.Presentation.Core.Resources;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Services.Input;

namespace SalmonEgg.Presentation.ViewModels.Settings;

public sealed partial class VoiceInputDiagnosticsProbeViewModel : ObservableObject
{
    private readonly IVoiceInputService _voiceInputService;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly IStringLocalizer<CoreStrings> _localizer;
    private readonly ILogger<VoiceInputDiagnosticsProbeViewModel> _logger;
    private CancellationTokenSource? _probeCts;
    private string? _activeRequestId;
    private DateTimeOffset? _startRequestedAt;
    private DateTimeOffset? _recognizerReadyAt;
    private DateTimeOffset? _firstPartialAt;
    private DateTimeOffset? _finalResultAt;
    private DateTimeOffset? _stopRequestedAt;
    private DateTimeOffset? _endedAt;
    private DateTimeOffset? _errorAt;
    private string? _errorMessage;

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

    public VoiceInputDiagnosticsProbeViewModel(
        IVoiceInputService voiceInputService,
        IUiDispatcher uiDispatcher,
        IStringLocalizer<CoreStrings> localizer,
        ILogger<VoiceInputDiagnosticsProbeViewModel> logger)
    {
        _voiceInputService = voiceInputService ?? throw new ArgumentNullException(nameof(voiceInputService));
        _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _probeStatusText = _localizer["VoiceDiagnostics_ProbeIdle"];
        _probeTimelineText = _localizer["VoiceDiagnostics_ProbeTimelinePending"];
        _probeCapturedText = _localizer["VoiceDiagnostics_ProbeNoCapturedText"];
        _voiceInputService.PartialResultReceived += OnPartialResultReceived;
        _voiceInputService.FinalResultReceived += OnFinalResultReceived;
        _voiceInputService.SessionEnded += OnSessionEnded;
        _voiceInputService.ErrorOccurred += OnErrorOccurred;
    }

    public bool CanStartProbe => !IsRunning;

    public bool CanStopProbe => IsRunning;

    [RelayCommand]
    private async Task StartProbeAsync()
    {
        if (IsRunning)
        {
            return;
        }

        if (!_voiceInputService.IsSupported)
        {
            await RunOnUiAsync(() =>
            {
                ProbeStatusText = _localizer["VoiceDiagnostics_Unsupported"];
                ProbeTimelineText = _localizer["VoiceDiagnostics_TimelineUnavailable"];
                ProbeCapturedText = _localizer["VoiceDiagnostics_ProbeNoCapturedText"];
            }).ConfigureAwait(false);
            return;
        }

        if (_voiceInputService.IsListening)
        {
            await RunOnUiAsync(() =>
            {
                ProbeStatusText = _localizer["VoiceDiagnostics_ProbeBusy"];
            }).ConfigureAwait(false);
            return;
        }

        await RunOnUiAsync(() =>
        {
            ResetProbeState();
            IsRunning = true;
            ProbeStatusText = _localizer["VoiceDiagnostics_ProbeAuthorizing"];
            ProbeTimelineText = _localizer["VoiceDiagnostics_ProbeTimelinePending"];
            ProbeCapturedText = _localizer["VoiceDiagnostics_ProbeNoCapturedText"];
        }).ConfigureAwait(false);

        TryDisposeProbeCts();
        _probeCts = new CancellationTokenSource();

        try
        {
            var permission = await _voiceInputService.EnsurePermissionAsync(_probeCts.Token).ConfigureAwait(false);
            if (!permission.IsGranted)
            {
                await _uiDispatcher.EnqueueAsync(() =>
                {
                    IsRunning = false;
                    ProbeStatusText = string.IsNullOrWhiteSpace(permission.Message)
                        ? _localizer["VoiceDiagnostics_PermissionDenied"]
                        : permission.Message!;
                }).ConfigureAwait(false);
                TryDisposeProbeCts();
                return;
            }

            var requestId = Guid.NewGuid().ToString("N");
            var languageTag = CultureInfo.CurrentUICulture.Name;
            await RunOnUiAsync(() =>
            {
                _activeRequestId = requestId;
                _startRequestedAt = DateTimeOffset.Now;
                ProbeStatusText = _localizer["VoiceDiagnostics_ProbeStarting"];
            }).ConfigureAwait(false);
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

            await _uiDispatcher.EnqueueAsync(() =>
            {
                if (!string.Equals(_activeRequestId, requestId, StringComparison.Ordinal))
                {
                    return;
                }

                _recognizerReadyAt = DateTimeOffset.Now;
                ProbeStatusText = _localizer["VoiceDiagnostics_ProbeListening"];
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
            _logger.LogWarning(ex, "Voice diagnostics probe failed before completion. RequestId={RequestId}", _activeRequestId);
            await RunOnUiAsync(() =>
            {
                _errorAt = DateTimeOffset.Now;
                _errorMessage = VoiceInputErrorMessageSanitizer.Normalize(ex.Message, "Voice input failed.");
                ProbeStatusText = string.Format(
                    CultureInfo.InvariantCulture,
                    _localizer["VoiceDiagnostics_SessionFailedWithMessage"],
                    _errorMessage);
                ProbeTimelineText = BuildTimelineText();
                IsRunning = false;
                _activeRequestId = null;
                TryDisposeProbeCts();
            }).ConfigureAwait(false);
        }
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
            ProbeStatusText = _localizer["VoiceDiagnostics_ProbeStopping"];
        }).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(requestId))
        {
            return;
        }

        _logger.LogInformation("Voice diagnostics probe stop requested. RequestId={RequestId}", requestId);

        try
        {
            try
            {
                _probeCts?.Cancel();
            }
            catch
            {
            }

            await _voiceInputService.StopAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Voice diagnostics probe stop failed. RequestId={RequestId}", requestId);
            await RunOnUiAsync(() =>
            {
                _errorAt = DateTimeOffset.Now;
                _errorMessage = VoiceInputErrorMessageSanitizer.Normalize(ex.Message, "Failed to stop voice input.");
                ProbeStatusText = string.Format(
                    CultureInfo.InvariantCulture,
                    _localizer["VoiceDiagnostics_SessionFailedWithMessage"],
                    _errorMessage);
                ProbeTimelineText = BuildTimelineText();
                IsRunning = false;
                _activeRequestId = null;
                TryDisposeProbeCts();
            }).ConfigureAwait(false);
        }
    }

    public async Task HandlePageUnloadedAsync()
    {
        if (IsRunning)
        {
            await StopProbeAsync().ConfigureAwait(false);
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
            ProbeCapturedText = result.Text;
            ProbeStatusText = _localizer["VoiceDiagnostics_SessionPartialOnly"];
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
            ProbeCapturedText = result.Text;
            ProbeStatusText = _localizer["VoiceDiagnostics_SessionFinalReceived"];
            ProbeTimelineText = BuildTimelineText();
        });

    private void OnSessionEnded(object? sender, VoiceInputSessionEndedResult result)
        => _ = _uiDispatcher.EnqueueAsync(() =>
        {
            if (!IsCurrentRequest(result.RequestId))
            {
                return;
            }

            _endedAt = DateTimeOffset.Now;
            ProbeStatusText = ResolveCompletedStatus();
            ProbeTimelineText = BuildTimelineText();
            IsRunning = false;
            _activeRequestId = null;
            TryDisposeProbeCts();
        });

    private void OnErrorOccurred(object? sender, VoiceInputErrorResult result)
        => _ = _uiDispatcher.EnqueueAsync(() =>
        {
            if (!IsCurrentRequest(result.RequestId))
            {
                return;
            }

            _errorAt = DateTimeOffset.Now;
            _errorMessage = result.Message;
            ProbeStatusText = string.Format(
                CultureInfo.InvariantCulture,
                _localizer["VoiceDiagnostics_SessionFailedWithMessage"],
                result.Message);
            ProbeTimelineText = BuildTimelineText();
            IsRunning = false;
            _activeRequestId = null;
            TryDisposeProbeCts();
        });

    private string ResolveCompletedStatus()
    {
        if (_errorAt is not null)
        {
            return string.IsNullOrWhiteSpace(_errorMessage)
                ? _localizer["VoiceDiagnostics_SessionFailed"]
                : string.Format(
                    CultureInfo.InvariantCulture,
                    _localizer["VoiceDiagnostics_SessionFailedWithMessage"],
                    _errorMessage);
        }

        if (_finalResultAt is not null)
        {
            return _localizer["VoiceDiagnostics_SessionFinalReceived"];
        }

        if (_firstPartialAt is not null)
        {
            return _localizer["VoiceDiagnostics_SessionPartialOnly"];
        }

        if (_recognizerReadyAt is not null)
        {
            return _localizer["VoiceDiagnostics_SessionReadyWithoutRecognition"];
        }

        return _localizer["VoiceDiagnostics_ProbeIdle"];
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
