#if WINDOWS
using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Services.Input;
using Windows.Devices.Enumeration;
using Windows.Globalization;
using Windows.Media.Capture;
using Windows.Media.Devices;
using Windows.Media.SpeechRecognition;
using Windows.System;

namespace SalmonEgg.Presentation.Services.Input;

public sealed class NativeVoiceInputService : IVoiceInputService, IVoiceInputRuntimeDiagnosticsSource
{
    private const int HResultPrivacyStatementDeclined = unchecked((int)0x80045509);
    private const int HResultNoCaptureDevices = -1072845856;
    private const int HResultAccessDenied = unchecked((int)0x80070005);
    private static readonly TimeSpan VoiceInputStopTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan VoiceInputCancelTimeout = TimeSpan.FromSeconds(2);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _sessionSignalSync = new();
    private readonly object _runtimeDiagnosticsSync = new();
    private readonly ILogger<NativeVoiceInputService> _logger;
    private readonly IUiDispatcher _uiDispatcher;
    private SpeechRecognizer? _recognizer;
    private CancellationTokenSource? _sessionCts;
    private Task? _sessionTask;
    private TaskCompletionSource<object?>? _sessionStarted;
    private TaskCompletionSource<object?>? _sessionCompletion;
    private string? _activeRequestId;
    private string? _stoppingRequestId;
    private bool _isListening;
    private bool _disposed;
    private AuthorizationTarget _authorizationTarget;
    private int _partialResultCount;
    private int _finalResultCount;
    private int _emptyPartialResultCount;
    private int _emptyFinalResultCount;
    private string? _runtimeRequestId;
    private DateTimeOffset? _runtimeStartRequestedAt;
    private DateTimeOffset? _runtimeRecognizerReadyAt;
    private DateTimeOffset? _runtimeFirstPartialAt;
    private DateTimeOffset? _runtimeFinalResultAt;
    private DateTimeOffset? _runtimeStopRequestedAt;
    private DateTimeOffset? _runtimeEndedAt;
    private DateTimeOffset? _runtimeErrorAt;
    private string? _runtimeErrorCode;
    private string? _runtimeErrorMessage;
    private string? _runtimeLanguageTag;
    private string? _runtimeCompletionStatus;
    private VoiceInputDiagnosticSession? _latestRuntimeSession;

    public NativeVoiceInputService(
        ILogger<NativeVoiceInputService> logger,
        IUiDispatcher uiDispatcher)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
    }

    public bool IsSupported => true;

    public bool IsListening => _isListening;

    public event EventHandler<VoiceInputPartialResult>? PartialResultReceived;

    public event EventHandler<VoiceInputFinalResult>? FinalResultReceived;

    public event EventHandler<VoiceInputSessionEndedResult>? SessionEnded;

    public event EventHandler<VoiceInputErrorResult>? ErrorOccurred;

    public async Task<VoiceInputPermissionResult> EnsurePermissionAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        try
        {
            await EnsureMicrophoneCaptureAccessAsync(requestId: null, cancellationToken).ConfigureAwait(false);
            return await GetPermissionStatusAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (TryCreateAuthorizationError(ex, out var permissionResult))
            {
                return permissionResult;
            }

            if (ex.HResult == HResultNoCaptureDevices)
            {
                return CreatePermissionDeniedResult(
                    AuthorizationTarget.None,
                    "No microphone was detected on this device.",
                    requiresAuthorization: false);
            }

            return CreatePermissionDeniedResult(
                AuthorizationTarget.Speech,
                VoiceInputErrorMessageSanitizer.Normalize(ex.Message, "Voice input permission check failed."),
                requiresAuthorization: false);
        }
    }

    public async Task<VoiceInputPermissionResult> GetPermissionStatusAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        try
        {
            using var recognizer = CreateRecognizer(CultureInfo.CurrentUICulture.Name);
            var compileResult = await recognizer.CompileConstraintsAsync().AsTask(cancellationToken).ConfigureAwait(false);
            return compileResult.Status switch
            {
                SpeechRecognitionResultStatus.Success => ClearAuthorizationTargetAndReturnGranted(),
                _ => CreatePermissionDeniedResultFromStatus(compileResult.Status)
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (TryCreateAuthorizationError(ex, out var permissionResult))
            {
                return permissionResult;
            }

            if (ex.HResult == HResultNoCaptureDevices)
            {
                return CreatePermissionDeniedResult(
                    AuthorizationTarget.None,
                    "No microphone was detected on this device.",
                    requiresAuthorization: false);
            }

            return CreatePermissionDeniedResult(
                AuthorizationTarget.Speech,
                VoiceInputErrorMessageSanitizer.Normalize(ex.Message, "Voice input permission check failed."),
                requiresAuthorization: false);
        }
    }

    public async Task<bool> TryRequestAuthorizationHelpAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var uri = GetAuthorizationSettingsUri();
        if (uri is null)
        {
            return false;
        }

        try
        {
            return await Launcher.LaunchUriAsync(uri).AsTask(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return false;
        }
    }

    public async Task StartAsync(VoiceInputSessionOptions options, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(options.RequestId))
        {
            throw new ArgumentException("Voice input request id cannot be empty.", nameof(options));
        }

        try
        {
            await EnsureMicrophoneCaptureAccessAsync(options.RequestId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw CreateStartFailureException(options.RequestId, ex);
        }

        Task sessionStartedTask;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (_isListening)
            {
                throw new InvalidOperationException("Voice input is already listening.");
            }

            _sessionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _sessionStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            _sessionCompletion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            _activeRequestId = options.RequestId;
            _stoppingRequestId = null;
            _isListening = true;
            ResetSessionCounters();
            BeginRuntimeSession(options.RequestId, NormalizeLanguageTag(options.LanguageTag));
            _sessionTask = RunRecognitionAsync(options, _sessionCts.Token);
            sessionStartedTask = GetSessionStartedTask();
        }
        finally
        {
            _gate.Release();
        }

        await sessionStartedTask.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureMicrophoneCaptureAccessAsync(string? requestId, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Voice input microphone preflight started. RequestId={RequestId}",
            requestId);

        try
        {
            await _uiDispatcher.EnqueueAsync(async () =>
            {
                using var capture = new MediaCapture();
                var settings = new MediaCaptureInitializationSettings
                {
                    StreamingCaptureMode = StreamingCaptureMode.Audio,
                    MediaCategory = MediaCategory.Speech
                };
                await capture.InitializeAsync(settings).AsTask(cancellationToken).ConfigureAwait(false);
            }).ConfigureAwait(false);

            _logger.LogInformation(
                "Voice input microphone preflight completed. RequestId={RequestId}",
                requestId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Voice input microphone preflight failed. RequestId={RequestId}",
                requestId);
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        SpeechRecognizer? recognizer;
        string? requestId;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_isListening)
            {
                return;
            }

            recognizer = _recognizer;
            requestId = _activeRequestId;
            _stoppingRequestId = requestId;
            MarkRuntimeStopRequested();
        }
        finally
        {
            _gate.Release();
        }

        _logger.LogInformation(
            "Voice input service stop initiated. RequestId={RequestId} RecognizerPresent={RecognizerPresent} SessionCompletionTaskCompleted={SessionCompletionTaskCompleted}",
            requestId,
            recognizer is not null,
            GetSessionCompletionTask().IsCompleted);

        if (recognizer is not null)
        {
            var stopCompleted = await TryStopRecognitionSessionAsync(
                recognizer,
                requestId,
                cancellationToken).ConfigureAwait(false);

            if (!stopCompleted && !string.IsNullOrWhiteSpace(requestId))
            {
                SetRuntimeCompletionStatus("StoppedByAppForcedEnd");
                _logger.LogWarning(
                    "Voice input stop did not complete in time. Forcing local session end. RequestId={RequestId}",
                    requestId);
            }
        }

        if (!string.IsNullOrWhiteSpace(requestId))
        {
            TrySignalSessionEnded(requestId);
        }

        _logger.LogInformation(
            "Voice input service stop returned. RequestId={RequestId}",
            requestId);
    }

    private async Task<bool> TryStopRecognitionSessionAsync(
        SpeechRecognizer recognizer,
        string? requestId,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Voice input graceful stop request started. RequestId={RequestId}",
            requestId);
        try
        {
            await recognizer.ContinuousRecognitionSession
                .StopAsync()
                .AsTask(cancellationToken)
                .WaitAsync(VoiceInputStopTimeout, cancellationToken)
                .ConfigureAwait(false);
            _logger.LogInformation(
                "Voice input graceful stop request completed. RequestId={RequestId}",
                requestId);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TimeoutException ex)
        {
            _logger.LogWarning(
                ex,
                "Voice input graceful stop timed out. RequestId={RequestId} TimeoutMs={TimeoutMs}",
                requestId,
                VoiceInputStopTimeout.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Voice input graceful stop failed. RequestId={RequestId}",
                requestId);
        }

        _logger.LogInformation(
            "Voice input cancel fallback started. RequestId={RequestId}",
            requestId);
        try
        {
            await recognizer.ContinuousRecognitionSession
                .CancelAsync()
                .AsTask(cancellationToken)
                .WaitAsync(VoiceInputCancelTimeout, cancellationToken)
                .ConfigureAwait(false);
            _logger.LogInformation(
                "Voice input cancel fallback completed. RequestId={RequestId}",
                requestId);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TimeoutException ex)
        {
            _logger.LogWarning(
                ex,
                "Voice input cancel fallback timed out. RequestId={RequestId} TimeoutMs={TimeoutMs}",
                requestId,
                VoiceInputCancelTimeout.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Voice input cancel fallback failed. RequestId={RequestId}",
                requestId);
        }

        return false;
    }

    private async Task RunRecognitionAsync(VoiceInputSessionOptions options, CancellationToken cancellationToken)
    {
        var requestId = options.RequestId;

        try
        {
            var recognizer = await CreateSessionRecognizerAsync(options.LanguageTag, cancellationToken).ConfigureAwait(false);
            var compileResult = await recognizer.CompileConstraintsAsync().AsTask(cancellationToken).ConfigureAwait(false);
            if (compileResult.Status != SpeechRecognitionResultStatus.Success)
            {
                SetRuntimeFailure(
                    CreateStartFailureException(compileResult.Status).Message,
                    $"CompileConstraints:{compileResult.Status}");
                _logger.LogWarning(
                    "Voice input constraints compilation failed. RequestId={RequestId} Status={Status}",
                    requestId,
                    compileResult.Status);
                if (TrySignalSessionStartFailure(
                    requestId,
                    CreateStartFailureException(compileResult.Status)))
                {
                    LogSessionSummary(FinalizeRuntimeSession(requestId, null, null));
                }
                return;
            }

            var sessionCompletion = GetSessionCompletionTask();
            await recognizer.ContinuousRecognitionSession.StartAsync().AsTask(cancellationToken).ConfigureAwait(false);
            MarkRuntimeRecognizerReady();
            TrySignalSessionStarted(requestId);
            await sessionCompletion.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            SetRuntimeCompletionStatus("Canceled");
            _logger.LogInformation(
                "Voice input recognition session canceled. RequestId={RequestId} Started={Started}",
                requestId,
                HasSessionStarted(requestId));
            if (!TrySignalSessionStartCanceled(requestId))
            {
                TrySignalSessionEnded(requestId);
            }
            else
            {
                LogSessionSummary(FinalizeRuntimeSession(requestId, null, null));
            }
        }
        catch (Exception ex)
        {
            SetRuntimeFailure(
                VoiceInputErrorMessageSanitizer.Normalize(ex.Message, "Voice input failed."),
                $"Failed:{ex.GetType().Name}",
                $"0x{unchecked((uint)ex.HResult):X8}");
            _logger.LogWarning(
                ex,
                "Voice input recognition session failed. RequestId={RequestId} Started={Started}",
                requestId,
                HasSessionStarted(requestId));
            if (!TrySignalSessionStartFailure(requestId, CreateStartFailureException(requestId, ex)))
            {
                TrySignalError(CreateErrorResult(requestId, ex));
            }
            else
            {
                LogSessionSummary(FinalizeRuntimeSession(requestId, null, null));
            }
        }
        finally
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (string.Equals(_activeRequestId, requestId, StringComparison.Ordinal))
                {
                    _isListening = false;
                    _activeRequestId = null;
                    _sessionTask = null;
                    _sessionStarted = null;
                    _sessionCompletion = null;
                    _stoppingRequestId = null;
                    try
                    {
                        _sessionCts?.Dispose();
                    }
                    catch
                    {
                    }
                    _sessionCts = null;
                }

                DisposeRecognizer();
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    private async Task<SpeechRecognizer> CreateSessionRecognizerAsync(string languageTag, CancellationToken cancellationToken)
    {
        var normalizedLanguage = NormalizeLanguageTag(languageTag);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DisposeRecognizer();
            _recognizer = CreateRecognizer(normalizedLanguage);
            _recognizer.HypothesisGenerated += OnHypothesisGenerated;
            _recognizer.ContinuousRecognitionSession.ResultGenerated += OnResultGenerated;
            _recognizer.ContinuousRecognitionSession.Completed += OnRecognitionCompleted;
            return _recognizer;
        }
        finally
        {
            _gate.Release();
        }
    }

    private void OnHypothesisGenerated(SpeechRecognizer sender, SpeechRecognitionHypothesisGeneratedEventArgs args)
    {
        var requestId = _activeRequestId;
        if (string.IsNullOrWhiteSpace(requestId))
        {
            return;
        }

        var text = args?.Hypothesis?.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            var emptyCount = Interlocked.Increment(ref _emptyPartialResultCount);
            MarkRuntimeEmptyPartialObserved();
            if (emptyCount == 1)
            {
                _logger.LogInformation(
                    "Voice input hypothesis callback received without usable text. RequestId={RequestId}",
                    requestId);
            }
            return;
        }

        var partialCount = Interlocked.Increment(ref _partialResultCount);
        MarkRuntimePartialObserved();
        if (partialCount == 1)
        {
            _logger.LogInformation(
                "Voice input first partial received. RequestId={RequestId} TextLength={TextLength}",
                requestId,
                text.Length);
        }

        PartialResultReceived?.Invoke(this, new VoiceInputPartialResult(requestId, text));
    }

    private void OnResultGenerated(SpeechContinuousRecognitionSession sender, SpeechContinuousRecognitionResultGeneratedEventArgs args)
    {
        var requestId = _activeRequestId;
        if (string.IsNullOrWhiteSpace(requestId))
        {
            return;
        }

        var text = args?.Result?.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            var emptyCount = Interlocked.Increment(ref _emptyFinalResultCount);
            MarkRuntimeEmptyFinalObserved();
            if (emptyCount == 1)
            {
                _logger.LogInformation(
                    "Voice input final-result callback received without usable text. RequestId={RequestId} Status={Status}",
                    requestId,
                    args?.Result?.Status);
            }
            return;
        }

        var finalCount = Interlocked.Increment(ref _finalResultCount);
        MarkRuntimeFinalObserved();
        if (finalCount == 1)
        {
            _logger.LogInformation(
                "Voice input first final result received. RequestId={RequestId} TextLength={TextLength} Status={Status}",
                requestId,
                text.Length,
                args?.Result?.Status);
        }

        FinalResultReceived?.Invoke(this, new VoiceInputFinalResult(requestId, text));
    }

    private void OnRecognitionCompleted(SpeechContinuousRecognitionSession sender, SpeechContinuousRecognitionCompletedEventArgs args)
    {
        var requestId = _activeRequestId;
        if (string.IsNullOrWhiteSpace(requestId))
        {
            return;
        }

        var completionStatus = args.Status.ToString();
        var stopRequested = string.Equals(_stoppingRequestId, requestId, StringComparison.Ordinal);
        var partialCount = Volatile.Read(ref _partialResultCount);
        var finalCount = Volatile.Read(ref _finalResultCount);
        var emptyPartialCount = Volatile.Read(ref _emptyPartialResultCount);
        var emptyFinalCount = Volatile.Read(ref _emptyFinalResultCount);

        SetRuntimeCompletionStatus(completionStatus);
        _logger.LogInformation(
            "Voice input recognition completed. RequestId={RequestId} Status={Status} StopRequested={StopRequested} PartialCount={PartialCount} FinalCount={FinalCount} EmptyPartialCount={EmptyPartialCount} EmptyFinalCount={EmptyFinalCount}",
            requestId,
            args.Status,
            stopRequested,
            partialCount,
            finalCount,
            emptyPartialCount,
            emptyFinalCount);

        if (VoiceInputRecognitionCompletionPolicy.ShouldTreatAsGracefulEnd(
                completionStatus,
                stopRequested,
                partialCount,
                finalCount))
        {
            TrySignalSessionEnded(requestId);
            return;
        }

        TrySignalError(new VoiceInputErrorResult(
            requestId,
            VoiceInputRecognitionCompletionPolicy.BuildFailureMessage(completionStatus),
            completionStatus));
    }

    private static SpeechRecognizer CreateRecognizer(string languageTag)
    {
        if (string.IsNullOrWhiteSpace(languageTag))
        {
            return new SpeechRecognizer();
        }

        try
        {
            return new SpeechRecognizer(new Language(languageTag));
        }
        catch
        {
            return new SpeechRecognizer();
        }
    }

    private static string NormalizeLanguageTag(string languageTag)
    {
        if (string.IsNullOrWhiteSpace(languageTag))
        {
            return CultureInfo.CurrentUICulture.Name;
        }

        return languageTag.Trim();
    }

    private void DisposeRecognizer()
    {
        if (_recognizer is null)
        {
            return;
        }

        try
        {
            _recognizer.HypothesisGenerated -= OnHypothesisGenerated;
            _recognizer.ContinuousRecognitionSession.ResultGenerated -= OnResultGenerated;
            _recognizer.ContinuousRecognitionSession.Completed -= OnRecognitionCompleted;
        }
        catch
        {
        }

        try
        {
            _recognizer.Dispose();
        }
        catch
        {
        }

        _recognizer = null;
    }

    private void RaiseError(VoiceInputErrorResult error)
    {
        ErrorOccurred?.Invoke(this, error);
    }

    private VoiceInputErrorResult CreateErrorResult(string requestId, Exception exception)
    {
        var errorCode = $"0x{unchecked((uint)exception.HResult):X8}";
        return exception.HResult switch
        {
            HResultPrivacyStatementDeclined => new VoiceInputErrorResult(
                requestId,
                "Windows online speech recognition is turned off. Enable Settings > Privacy & security > Speech, then try again.",
                errorCode,
                RequiresAuthorization: SetAuthorizationTarget(AuthorizationTarget.Speech)),
            HResultAccessDenied => new VoiceInputErrorResult(
                requestId,
                "Microphone access is blocked for SalmonEgg. Enable it in Settings > Privacy & security > Microphone, then try again.",
                errorCode,
                RequiresAuthorization: SetAuthorizationTarget(AuthorizationTarget.Microphone)),
            HResultNoCaptureDevices => new VoiceInputErrorResult(
                requestId,
                "No microphone was detected on this device.",
                errorCode),
            _ => new VoiceInputErrorResult(
                requestId,
                VoiceInputErrorMessageSanitizer.Normalize(exception.Message, "Voice input failed."),
                errorCode)
        };
    }

    private VoiceInputStartFailureException CreateStartFailureException(SpeechRecognitionResultStatus status)
    {
        var permissionResult = CreatePermissionDeniedResultFromStatus(status);
        var message = permissionResult.Message ?? $"Unable to prepare voice recognition: {status}.";
        return new VoiceInputStartFailureException(
            message,
            permissionResult.RequiresAuthorization);
    }

    private VoiceInputStartFailureException CreateStartFailureException(string requestId, Exception exception)
    {
        var error = CreateErrorResult(requestId, exception);
        return new VoiceInputStartFailureException(
            error.Message,
            error.RequiresAuthorization,
            exception);
    }

    private Task GetSessionCompletionTask()
    {
        lock (_sessionSignalSync)
        {
            return _sessionCompletion?.Task ?? Task.CompletedTask;
        }
    }

    private Task GetSessionStartedTask()
    {
        lock (_sessionSignalSync)
        {
            return _sessionStarted?.Task ?? Task.CompletedTask;
        }
    }

    private bool HasSessionStarted(string requestId)
    {
        lock (_sessionSignalSync)
        {
            return !string.IsNullOrWhiteSpace(requestId)
                && string.Equals(_activeRequestId, requestId, StringComparison.Ordinal)
                && _sessionStarted?.Task.IsCompletedSuccessfully == true;
        }
    }

    private bool TrySignalSessionStarted(string requestId)
    {
        lock (_sessionSignalSync)
        {
            if (string.IsNullOrWhiteSpace(requestId)
                || !string.Equals(_activeRequestId, requestId, StringComparison.Ordinal)
                || _sessionStarted is null)
            {
                return false;
            }

            return _sessionStarted.TrySetResult(null);
        }
    }

    private bool TrySignalSessionStartFailure(string requestId, Exception exception)
    {
        lock (_sessionSignalSync)
        {
            if (string.IsNullOrWhiteSpace(requestId)
                || !string.Equals(_activeRequestId, requestId, StringComparison.Ordinal)
                || _sessionStarted is null)
            {
                return false;
            }

            return _sessionStarted.TrySetException(exception);
        }
    }

    private bool TrySignalSessionStartCanceled(string requestId)
    {
        lock (_sessionSignalSync)
        {
            if (string.IsNullOrWhiteSpace(requestId)
                || !string.Equals(_activeRequestId, requestId, StringComparison.Ordinal)
                || _sessionStarted is null)
            {
                return false;
            }

            return _sessionStarted.TrySetCanceled();
        }
    }

    private void TrySignalSessionEnded(string requestId)
    {
        var shouldRaise = TryCompleteSessionSignal(requestId);
        if (shouldRaise)
        {
            _logger.LogInformation(
                "Voice input session completion signal raised. RequestId={RequestId} Kind={Kind}",
                requestId,
                "Ended");
            LogSessionSummary(FinalizeRuntimeSession(requestId, null, null));
            SessionEnded?.Invoke(this, new VoiceInputSessionEndedResult(requestId));
        }
    }

    private void TrySignalError(VoiceInputErrorResult error)
    {
        var shouldRaise = TryCompleteSessionSignal(error.RequestId);
        if (shouldRaise)
        {
            _logger.LogInformation(
                "Voice input session completion signal raised. RequestId={RequestId} Kind={Kind}",
                error.RequestId,
                "Error");
            LogSessionSummary(FinalizeRuntimeSession(error.RequestId, error.ErrorCode, error.Message));
            RaiseError(error);
        }
    }

    public async Task<VoiceInputRuntimeDiagnostics> GetRuntimeDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        var (defaultInputDeviceName, defaultInputDeviceId) = await GetDefaultInputDeviceAsync(cancellationToken).ConfigureAwait(false);
        lock (_runtimeDiagnosticsSync)
        {
            return new VoiceInputRuntimeDiagnostics(
                defaultInputDeviceName,
                defaultInputDeviceId,
                _runtimeRequestId is not null ? BuildRuntimeSessionSnapshotUnsafe() : _latestRuntimeSession);
        }
    }

    private bool TryCompleteSessionSignal(string requestId)
    {
        lock (_sessionSignalSync)
        {
            if (string.IsNullOrWhiteSpace(requestId)
                || !string.Equals(_activeRequestId, requestId, StringComparison.Ordinal)
                || _sessionCompletion is null)
            {
                return false;
            }

            return _sessionCompletion.TrySetResult(null);
        }
    }

    private VoiceInputPermissionResult ClearAuthorizationTargetAndReturnGranted()
    {
        _authorizationTarget = AuthorizationTarget.None;
        return VoiceInputPermissionResult.Granted();
    }

    private VoiceInputPermissionResult CreatePermissionDeniedResult(
        AuthorizationTarget target,
        string message,
        bool requiresAuthorization = true)
    {
        if (requiresAuthorization)
        {
            _authorizationTarget = target;
        }

        return new VoiceInputPermissionResult(
            VoiceInputPermissionStatus.Denied,
            message,
            RequiresAuthorization: requiresAuthorization);
    }

    private VoiceInputPermissionResult CreatePermissionDeniedResultFromStatus(SpeechRecognitionResultStatus status)
    {
        var statusName = status.ToString();
        if (statusName.IndexOf("Privacy", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return CreatePermissionDeniedResult(
                AuthorizationTarget.Speech,
                "Windows online speech recognition is turned off. Enable Settings > Privacy & security > Speech, then try again.");
        }

        if (statusName.IndexOf("Access", StringComparison.OrdinalIgnoreCase) >= 0
            || statusName.IndexOf("Permission", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return CreatePermissionDeniedResult(
                AuthorizationTarget.Microphone,
                "Microphone access is blocked for SalmonEgg. Enable Settings > Privacy & security > Microphone, then try again.");
        }

        return CreatePermissionDeniedResult(
            AuthorizationTarget.Speech,
            $"Unable to prepare voice recognition: {statusName}.",
            requiresAuthorization: false);
    }

    private bool TryCreateAuthorizationError(Exception exception, out VoiceInputPermissionResult result)
    {
        result = exception.HResult switch
        {
            HResultPrivacyStatementDeclined => CreatePermissionDeniedResult(
                AuthorizationTarget.Speech,
                "Windows online speech recognition is turned off. Enable Settings > Privacy & security > Speech, then try again."),
            HResultAccessDenied => CreatePermissionDeniedResult(
                AuthorizationTarget.Microphone,
                "Microphone access is blocked for SalmonEgg. Enable Settings > Privacy & security > Microphone, then try again."),
            _ => default
        };

        return result.Status == VoiceInputPermissionStatus.Denied;
    }

    private bool SetAuthorizationTarget(AuthorizationTarget target)
    {
        _authorizationTarget = target;
        return true;
    }

    private void ResetSessionCounters()
    {
        Interlocked.Exchange(ref _partialResultCount, 0);
        Interlocked.Exchange(ref _finalResultCount, 0);
        Interlocked.Exchange(ref _emptyPartialResultCount, 0);
        Interlocked.Exchange(ref _emptyFinalResultCount, 0);
    }

    private void BeginRuntimeSession(string requestId, string normalizedLanguageTag)
    {
        lock (_runtimeDiagnosticsSync)
        {
            _runtimeRequestId = requestId;
            _runtimeStartRequestedAt = DateTimeOffset.Now;
            _runtimeRecognizerReadyAt = null;
            _runtimeFirstPartialAt = null;
            _runtimeFinalResultAt = null;
            _runtimeStopRequestedAt = null;
            _runtimeEndedAt = null;
            _runtimeErrorAt = null;
            _runtimeErrorCode = null;
            _runtimeErrorMessage = null;
            _runtimeLanguageTag = normalizedLanguageTag;
            _runtimeCompletionStatus = null;
        }
    }

    private void MarkRuntimeRecognizerReady()
    {
        lock (_runtimeDiagnosticsSync)
        {
            _runtimeRecognizerReadyAt ??= DateTimeOffset.Now;
        }
    }

    private void MarkRuntimePartialObserved()
    {
        lock (_runtimeDiagnosticsSync)
        {
            _runtimeFirstPartialAt ??= DateTimeOffset.Now;
        }
    }

    private void MarkRuntimeFinalObserved()
    {
        lock (_runtimeDiagnosticsSync)
        {
            _runtimeFinalResultAt ??= DateTimeOffset.Now;
        }
    }

    private void MarkRuntimeEmptyPartialObserved()
    {
        lock (_runtimeDiagnosticsSync)
        {
            _runtimeRecognizerReadyAt ??= DateTimeOffset.Now;
        }
    }

    private void MarkRuntimeEmptyFinalObserved()
    {
        lock (_runtimeDiagnosticsSync)
        {
            _runtimeRecognizerReadyAt ??= DateTimeOffset.Now;
        }
    }

    private void MarkRuntimeStopRequested()
    {
        lock (_runtimeDiagnosticsSync)
        {
            _runtimeStopRequestedAt ??= DateTimeOffset.Now;
            _runtimeCompletionStatus ??= "StoppedByApp";
        }
    }

    private void SetRuntimeCompletionStatus(string completionStatus)
    {
        lock (_runtimeDiagnosticsSync)
        {
            _runtimeCompletionStatus = completionStatus;
        }
    }

    private void SetRuntimeFailure(string errorMessage, string? completionStatus, string? errorCode = null)
    {
        lock (_runtimeDiagnosticsSync)
        {
            _runtimeErrorAt ??= DateTimeOffset.Now;
            _runtimeErrorCode ??= errorCode;
            _runtimeErrorMessage ??= errorMessage;
            if (!string.IsNullOrWhiteSpace(completionStatus))
            {
                _runtimeCompletionStatus = completionStatus;
            }
        }
    }

    private VoiceInputDiagnosticSession FinalizeRuntimeSession(string requestId, string? errorCode, string? errorMessage)
    {
        lock (_runtimeDiagnosticsSync)
        {
            if (!string.Equals(_runtimeRequestId, requestId, StringComparison.Ordinal))
            {
                return _latestRuntimeSession
                    ?? new VoiceInputDiagnosticSession(
                        requestId,
                        VoiceInputDiagnosticSessionOutcome.Unknown,
                        null,
                        null,
                        null,
                        null,
                        null,
                        null,
                        null,
                        errorCode,
                        errorMessage,
                        null,
                        0,
                        0,
                        0,
                        0,
                        null);
            }

            _runtimeEndedAt ??= DateTimeOffset.Now;
            _runtimeErrorCode ??= errorCode;
            _runtimeErrorMessage ??= errorMessage;
            _runtimeErrorAt ??= string.IsNullOrWhiteSpace(_runtimeErrorMessage) ? null : DateTimeOffset.Now;
            _latestRuntimeSession = BuildRuntimeSessionSnapshotUnsafe();
            _runtimeRequestId = null;
            return _latestRuntimeSession;
        }
    }

    private VoiceInputDiagnosticSession BuildRuntimeSessionSnapshotUnsafe()
    {
        var outcome = DetermineRuntimeOutcomeUnsafe();
        return new VoiceInputDiagnosticSession(
            RequestId: _runtimeRequestId ?? _latestRuntimeSession?.RequestId ?? string.Empty,
            Outcome: outcome,
            StartRequestedAt: _runtimeStartRequestedAt,
            RecognizerReadyAt: _runtimeRecognizerReadyAt,
            FirstPartialAt: _runtimeFirstPartialAt,
            FinalResultAt: _runtimeFinalResultAt,
            StopRequestedAt: _runtimeStopRequestedAt,
            EndedAt: _runtimeEndedAt,
            ErrorAt: _runtimeErrorAt,
            ErrorCode: _runtimeErrorCode,
            ErrorMessage: _runtimeErrorMessage,
            LanguageTag: _runtimeLanguageTag,
            PartialResultCount: Volatile.Read(ref _partialResultCount),
            FinalResultCount: Volatile.Read(ref _finalResultCount),
            EmptyPartialResultCount: Volatile.Read(ref _emptyPartialResultCount),
            EmptyFinalResultCount: Volatile.Read(ref _emptyFinalResultCount),
            CompletionStatus: _runtimeCompletionStatus);
    }

    private VoiceInputDiagnosticSessionOutcome DetermineRuntimeOutcomeUnsafe()
    {
        if (_runtimeErrorAt is not null || !string.IsNullOrWhiteSpace(_runtimeErrorMessage))
        {
            return VoiceInputDiagnosticSessionOutcome.Failed;
        }

        if (_runtimeFinalResultAt is not null || Volatile.Read(ref _finalResultCount) > 0)
        {
            return VoiceInputDiagnosticSessionOutcome.FinalResultReceived;
        }

        if (_runtimeFirstPartialAt is not null || Volatile.Read(ref _partialResultCount) > 0)
        {
            return VoiceInputDiagnosticSessionOutcome.PartialResultReceived;
        }

        if (_runtimeRecognizerReadyAt is not null)
        {
            return VoiceInputDiagnosticSessionOutcome.ReadyWithoutRecognition;
        }

        if (_runtimeStartRequestedAt is not null)
        {
            return VoiceInputDiagnosticSessionOutcome.StartRequested;
        }

        return VoiceInputDiagnosticSessionOutcome.Unknown;
    }

    private void LogSessionSummary(VoiceInputDiagnosticSession session)
    {
        _logger.LogInformation(
            "Voice input session summary. RequestId={RequestId} Outcome={Outcome} CompletionStatus={CompletionStatus} PartialCount={PartialCount} FinalCount={FinalCount} EmptyPartialCount={EmptyPartialCount} EmptyFinalCount={EmptyFinalCount}",
            session.RequestId,
            session.Outcome,
            session.CompletionStatus,
            session.PartialResultCount,
            session.FinalResultCount,
            session.EmptyPartialResultCount,
            session.EmptyFinalResultCount);
    }

    private static async Task<(string? DeviceName, string? DeviceId)> GetDefaultInputDeviceAsync(CancellationToken cancellationToken)
    {
        try
        {
            var deviceId = MediaDevice.GetDefaultAudioCaptureId(AudioDeviceRole.Default);
            if (string.IsNullOrWhiteSpace(deviceId))
            {
                return (null, null);
            }

            var info = await DeviceInformation.CreateFromIdAsync(deviceId).AsTask(cancellationToken).ConfigureAwait(false);
            return (info?.Name, deviceId);
        }
        catch
        {
            return (null, null);
        }
    }

    private Uri? GetAuthorizationSettingsUri()
        => _authorizationTarget switch
        {
            AuthorizationTarget.Microphone => new Uri("ms-settings:privacy-microphone"),
            AuthorizationTarget.Speech => new Uri("ms-settings:privacy-speech"),
            _ => null
        };

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _sessionCts?.Cancel();
        }
        catch
        {
        }

        DisposeRecognizer();
        try
        {
            _sessionCts?.Dispose();
        }
        catch
        {
        }

        _sessionCts = null;
        _sessionTask = null;
        _activeRequestId = null;
        _isListening = false;
        _gate.Dispose();
    }

    private enum AuthorizationTarget
    {
        None = 0,
        Microphone = 1,
        Speech = 2
    }
}
#endif
