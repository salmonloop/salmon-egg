using System;
using System.Globalization;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SalmonEgg.Presentation.Core.Resources;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Services.Input;

namespace SalmonEgg.Presentation.ViewModels.Settings;

public sealed partial class VoiceInputDiagnosticsViewModel : ObservableObject
{
    private readonly IVoiceInputDiagnosticsService _service;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly IStringLocalizer<CoreStrings> _localizer;
    private readonly ILogger<VoiceInputDiagnosticsViewModel> _logger;

    public VoiceInputDiagnosticsProbeViewModel Probe { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenAuthorizationHelpCommand))]
    [NotifyPropertyChangedFor(nameof(CanOpenAuthorizationHelp))]
    private bool _requiresAuthorization;

    [ObservableProperty]
    private string _supportStatusText;

    [ObservableProperty]
    private string _permissionStatusText;

    [ObservableProperty]
    private string _currentLanguageTagText;

    [ObservableProperty]
    private string _inputDeviceText;

    [ObservableProperty]
    private string _sessionStatusText;

    [ObservableProperty]
    private string _callbackObservationText;

    [ObservableProperty]
    private string _timelineText;

    [ObservableProperty]
    private string _recommendationText;

    public VoiceInputDiagnosticsViewModel(
        IVoiceInputDiagnosticsService service,
        VoiceInputDiagnosticsProbeViewModel probe,
        IUiDispatcher uiDispatcher,
        IStringLocalizer<CoreStrings> localizer,
        ILogger<VoiceInputDiagnosticsViewModel> logger)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        Probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _supportStatusText = _localizer["VoiceDiagnostics_PendingRefresh"];
        _permissionStatusText = _localizer["VoiceDiagnostics_PendingRefresh"];
        _currentLanguageTagText = CultureInfo.CurrentUICulture.Name;
        _inputDeviceText = _localizer["VoiceDiagnostics_PendingRefresh"];
        _sessionStatusText = _localizer["VoiceDiagnostics_NoRecentSession"];
        _callbackObservationText = _localizer["VoiceDiagnostics_CallbackObservationUnavailable"];
        _timelineText = _localizer["VoiceDiagnostics_TimelineUnavailable"];
        _recommendationText = _localizer["VoiceDiagnostics_RecommendationNoRecentSession"];
    }

    public bool CanOpenAuthorizationHelp => RequiresAuthorization;

    public Task HandlePageUnloadedAsync()
        => Probe.HandlePageUnloadedAsync();

    [RelayCommand]
    private async Task RefreshSnapshotAsync()
    {
        try
        {
            var snapshot = await _service.GetSnapshotAsync().ConfigureAwait(false);
            await RunOnUiAsync(() => ApplySnapshot(snapshot)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Voice diagnostics refresh failed.");
            await RunOnUiAsync(() =>
            {
                SupportStatusText = _localizer["VoiceDiagnostics_RefreshFailed"];
                PermissionStatusText = _localizer["VoiceDiagnostics_RefreshFailed"];
                InputDeviceText = _localizer["VoiceDiagnostics_RefreshFailed"];
                SessionStatusText = _localizer["VoiceDiagnostics_RefreshFailed"];
                CallbackObservationText = _localizer["VoiceDiagnostics_RefreshFailed"];
                TimelineText = _localizer["VoiceDiagnostics_TimelineUnavailable"];
                RecommendationText = _localizer["VoiceDiagnostics_RecommendationRefreshFailed"];
                RequiresAuthorization = false;
            }).ConfigureAwait(false);
        }
    }

    [RelayCommand(CanExecute = nameof(CanOpenAuthorizationHelp))]
    private async Task OpenAuthorizationHelpAsync()
    {
        _ = await _service.TryOpenAuthorizationSettingsAsync().ConfigureAwait(false);
    }

    private void ApplySnapshot(VoiceInputDiagnosticsSnapshot snapshot)
    {
        SupportStatusText = snapshot.IsSupported
            ? _localizer["VoiceDiagnostics_Supported"]
            : _localizer["VoiceDiagnostics_Unsupported"];
        PermissionStatusText = ResolvePermissionStatus(snapshot.Permission);
        CurrentLanguageTagText = string.IsNullOrWhiteSpace(snapshot.CurrentLanguageTag)
            ? _localizer["VoiceDiagnostics_UnknownLanguage"]
            : snapshot.CurrentLanguageTag;
        InputDeviceText = string.IsNullOrWhiteSpace(snapshot.DefaultInputDeviceName)
            ? _localizer["VoiceDiagnostics_InputDeviceUnavailable"]
            : snapshot.DefaultInputDeviceName!;
        RequiresAuthorization = snapshot.Permission.RequiresAuthorization;

        var session = snapshot.LatestSession;
        if (session is null)
        {
            SessionStatusText = _localizer["VoiceDiagnostics_NoRecentSession"];
            CallbackObservationText = _localizer["VoiceDiagnostics_CallbackObservationUnavailable"];
            TimelineText = _localizer["VoiceDiagnostics_TimelineUnavailable"];
            RecommendationText = snapshot.Permission.RequiresAuthorization
                ? _localizer["VoiceDiagnostics_RecommendationRequiresAuthorization"]
                : _localizer["VoiceDiagnostics_RecommendationNoRecentSession"];
            return;
        }

        SessionStatusText = ResolveSessionStatus(session);
        CallbackObservationText = ResolveCallbackObservation(session);
        TimelineText = ResolveTimelineText(session);
        RecommendationText = ResolveRecommendation(snapshot, session);
    }

    private string ResolvePermissionStatus(VoiceInputPermissionResult permission)
    {
        return permission.Status switch
        {
            VoiceInputPermissionStatus.Granted => _localizer["VoiceDiagnostics_PermissionGranted"],
            VoiceInputPermissionStatus.Denied => string.IsNullOrWhiteSpace(permission.Message)
                ? _localizer["VoiceDiagnostics_PermissionDenied"]
                : permission.Message!,
            _ => string.IsNullOrWhiteSpace(permission.Message)
                ? _localizer["VoiceDiagnostics_Unsupported"]
                : permission.Message!
        };
    }

    private string ResolveSessionStatus(VoiceInputDiagnosticSession session)
    {
        return session.Outcome switch
        {
            VoiceInputDiagnosticSessionOutcome.FinalResultReceived => _localizer["VoiceDiagnostics_SessionFinalReceived"],
            VoiceInputDiagnosticSessionOutcome.PartialResultReceived => _localizer["VoiceDiagnostics_SessionPartialOnly"],
            VoiceInputDiagnosticSessionOutcome.ReadyWithoutRecognition => _localizer["VoiceDiagnostics_SessionReadyWithoutRecognition"],
            VoiceInputDiagnosticSessionOutcome.StartRequested => _localizer["VoiceDiagnostics_SessionStarting"],
            VoiceInputDiagnosticSessionOutcome.Failed => string.IsNullOrWhiteSpace(session.ErrorMessage)
                ? _localizer["VoiceDiagnostics_SessionFailed"]
                : string.Format(
                    CultureInfo.InvariantCulture,
                    _localizer["VoiceDiagnostics_SessionFailedWithMessage"],
                    session.ErrorMessage),
            _ => _localizer["VoiceDiagnostics_SessionUnknown"]
        };
    }

    private string ResolveTimelineText(VoiceInputDiagnosticSession session)
    {
        if (session.StartRequestedAt is not null && session.RecognizerReadyAt is not null)
        {
            var startToReady = session.RecognizerReadyAt.Value - session.StartRequestedAt.Value;
            if (session.Outcome == VoiceInputDiagnosticSessionOutcome.ReadyWithoutRecognition)
            {
                var endAnchor = session.EndedAt ?? session.StopRequestedAt;
                if (endAnchor is not null)
                {
                    var readyToEnd = endAnchor.Value - session.RecognizerReadyAt.Value;
                    return string.Format(
                        CultureInfo.InvariantCulture,
                        _localizer["VoiceDiagnostics_TimelineReadyWithoutRecognitionFormat"],
                        startToReady.TotalSeconds,
                        readyToEnd.TotalSeconds);
                }
            }

            if (session.FinalResultAt is not null)
            {
                var readyToFinal = session.FinalResultAt.Value - session.RecognizerReadyAt.Value;
                return string.Format(
                    CultureInfo.InvariantCulture,
                    _localizer["VoiceDiagnostics_TimelineFinalFormat"],
                    startToReady.TotalSeconds,
                    readyToFinal.TotalSeconds);
            }

            if (session.FirstPartialAt is not null)
            {
                var readyToPartial = session.FirstPartialAt.Value - session.RecognizerReadyAt.Value;
                return string.Format(
                    CultureInfo.InvariantCulture,
                    _localizer["VoiceDiagnostics_TimelinePartialFormat"],
                    startToReady.TotalSeconds,
                    readyToPartial.TotalSeconds);
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                _localizer["VoiceDiagnostics_TimelineStartToReadyFormat"],
                startToReady.TotalSeconds);
        }

        return _localizer["VoiceDiagnostics_TimelineUnavailable"];
    }

    private string ResolveCallbackObservation(VoiceInputDiagnosticSession session)
    {
        if (session.PartialResultCount == 0
            && session.FinalResultCount == 0
            && session.EmptyPartialResultCount == 0
            && session.EmptyFinalResultCount == 0)
        {
            return _localizer["VoiceDiagnostics_CallbackObservationNone"];
        }

        if (session.PartialResultCount == 0
            && session.FinalResultCount == 0)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                _localizer["VoiceDiagnostics_CallbackObservationEmptyOnlyFormat"],
                session.EmptyPartialResultCount,
                session.EmptyFinalResultCount);
        }

        return string.Format(
            CultureInfo.InvariantCulture,
            _localizer["VoiceDiagnostics_CallbackObservationFormat"],
            session.PartialResultCount,
            session.FinalResultCount,
            session.EmptyPartialResultCount,
            session.EmptyFinalResultCount);
    }

    private string ResolveRecommendation(VoiceInputDiagnosticsSnapshot snapshot, VoiceInputDiagnosticSession session)
    {
        if (snapshot.Permission.RequiresAuthorization)
        {
            return _localizer["VoiceDiagnostics_RecommendationRequiresAuthorization"];
        }

        if (session.Outcome == VoiceInputDiagnosticSessionOutcome.ReadyWithoutRecognition)
        {
            var languageTag = string.IsNullOrWhiteSpace(session.LanguageTag)
                ? snapshot.CurrentLanguageTag
                : session.LanguageTag!;
            return string.Format(
                CultureInfo.InvariantCulture,
                _localizer["VoiceDiagnostics_RecommendationReadyWithoutRecognitionFormat"],
                languageTag);
        }

        if (session.Outcome == VoiceInputDiagnosticSessionOutcome.Failed)
        {
            return _localizer["VoiceDiagnostics_RecommendationFailed"];
        }

        return _localizer["VoiceDiagnostics_RecommendationGeneric"];
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
