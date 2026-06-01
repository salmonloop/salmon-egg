#if WINDOWS
using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Presentation.Core.Services.Input;
using Windows.Globalization;
using Windows.Media.SpeechRecognition;
using Windows.System;

namespace SalmonEgg.Presentation.Services.Input;

public sealed class NativeVoiceInputService : IVoiceInputService
{
    private const int HResultPrivacyStatementDeclined = unchecked((int)0x80045509);
    private const int HResultNoCaptureDevices = -1072845856;
    private const int HResultAccessDenied = unchecked((int)0x80070005);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _sessionSignalSync = new();
    private SpeechRecognizer? _recognizer;
    private CancellationTokenSource? _sessionCts;
    private Task? _sessionTask;
    private TaskCompletionSource<object?>? _sessionCompletion;
    private string? _activeRequestId;
    private string? _activeLanguageTag;
    private string? _stoppingRequestId;
    private bool _isListening;
    private bool _disposed;
    private AuthorizationTarget _authorizationTarget;

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

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (_isListening)
            {
                throw new InvalidOperationException("Voice input is already listening.");
            }

            _sessionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _sessionCompletion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            _activeRequestId = options.RequestId;
            _stoppingRequestId = null;
            _isListening = true;
            _sessionTask = RunRecognitionAsync(options, _sessionCts.Token);
        }
        finally
        {
            _gate.Release();
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
        }
        finally
        {
            _gate.Release();
        }

        if (recognizer is not null)
        {
            try
            {
                await recognizer.ContinuousRecognitionSession.CancelAsync().AsTask(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // No-op: cancel can race with natural completion.
            }
        }

        if (!string.IsNullOrWhiteSpace(requestId))
        {
            TrySignalSessionEnded(requestId);
        }
    }

    private async Task RunRecognitionAsync(VoiceInputSessionOptions options, CancellationToken cancellationToken)
    {
        var requestId = options.RequestId;

        try
        {
            var recognizer = await EnsureRecognizerAsync(options.LanguageTag, cancellationToken).ConfigureAwait(false);
            var compileResult = await recognizer.CompileConstraintsAsync().AsTask(cancellationToken).ConfigureAwait(false);
            if (compileResult.Status != SpeechRecognitionResultStatus.Success)
            {
                TrySignalError(new VoiceInputErrorResult(
                    requestId,
                    $"Unable to prepare voice recognition: {compileResult.Status}.",
                    compileResult.Status.ToString()));
                return;
            }

            var sessionCompletion = GetSessionCompletionTask();
            await recognizer.ContinuousRecognitionSession.StartAsync().AsTask(cancellationToken).ConfigureAwait(false);
            await sessionCompletion.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TrySignalSessionEnded(requestId);
        }
        catch (Exception ex)
        {
            TrySignalError(CreateErrorResult(requestId, ex));
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
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    private async Task<SpeechRecognizer> EnsureRecognizerAsync(string languageTag, CancellationToken cancellationToken)
    {
        var normalizedLanguage = NormalizeLanguageTag(languageTag);
        if (_recognizer is not null
            && string.Equals(_activeLanguageTag, normalizedLanguage, StringComparison.OrdinalIgnoreCase))
        {
            return _recognizer;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_recognizer is not null
                && string.Equals(_activeLanguageTag, normalizedLanguage, StringComparison.OrdinalIgnoreCase))
            {
                return _recognizer;
            }

            DisposeRecognizer();
            _recognizer = CreateRecognizer(normalizedLanguage);
            _recognizer.HypothesisGenerated += OnHypothesisGenerated;
            _recognizer.ContinuousRecognitionSession.ResultGenerated += OnResultGenerated;
            _recognizer.ContinuousRecognitionSession.Completed += OnRecognitionCompleted;
            _activeLanguageTag = normalizedLanguage;
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
            return;
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
            return;
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

        if (string.Equals(_stoppingRequestId, requestId, StringComparison.Ordinal)
            || args.Status == SpeechRecognitionResultStatus.Success)
        {
            TrySignalSessionEnded(requestId);
            return;
        }

        TrySignalError(new VoiceInputErrorResult(
            requestId,
            $"Voice recognition ended: {args.Status}.",
            args.Status.ToString()));
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
        _activeLanguageTag = null;
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

    private Task GetSessionCompletionTask()
    {
        lock (_sessionSignalSync)
        {
            return _sessionCompletion?.Task ?? Task.CompletedTask;
        }
    }

    private void TrySignalSessionEnded(string requestId)
    {
        var shouldRaise = TryCompleteSessionSignal(requestId);
        if (shouldRaise)
        {
            SessionEnded?.Invoke(this, new VoiceInputSessionEndedResult(requestId));
        }
    }

    private void TrySignalError(VoiceInputErrorResult error)
    {
        var shouldRaise = TryCompleteSessionSignal(error.RequestId);
        if (shouldRaise)
        {
            RaiseError(error);
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
