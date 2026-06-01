using System;
using System.Globalization;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SalmonEgg.Presentation.Core.Resources;
using SalmonEgg.Presentation.Core.Services.Input;

namespace SalmonEgg.Presentation.ViewModels.Settings;

public sealed partial class VoiceInputDiagnosticsViewModel : ObservableObject
{
    private readonly IVoiceInputDiagnosticsService _service;
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
    private string _sessionStatusText;

    [ObservableProperty]
    private string _timelineText;

    [ObservableProperty]
    private string _recommendationText;

    public VoiceInputDiagnosticsViewModel(
        IVoiceInputDiagnosticsService service,
        VoiceInputDiagnosticsProbeViewModel probe,
        IStringLocalizer<CoreStrings> localizer,
        ILogger<VoiceInputDiagnosticsViewModel> logger)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        Probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _supportStatusText = _localizer["VoiceDiagnostics_PendingRefresh"];
        _permissionStatusText = _localizer["VoiceDiagnostics_PendingRefresh"];
        _currentLanguageTagText = CultureInfo.CurrentUICulture.Name;
        _sessionStatusText = _localizer["VoiceDiagnostics_NoRecentSession"];
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
            ApplySnapshot(snapshot);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Voice diagnostics refresh failed.");
            SupportStatusText = _localizer["VoiceDiagnostics_RefreshFailed"];
            PermissionStatusText = _localizer["VoiceDiagnostics_RefreshFailed"];
            SessionStatusText = _localizer["VoiceDiagnostics_RefreshFailed"];
            TimelineText = _localizer["VoiceDiagnostics_TimelineUnavailable"];
            RecommendationText = _localizer["VoiceDiagnostics_RecommendationRefreshFailed"];
            RequiresAuthorization = false;
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
        RequiresAuthorization = snapshot.Permission.RequiresAuthorization;

        var session = snapshot.LatestSession;
        if (session is null)
        {
            SessionStatusText = _localizer["VoiceDiagnostics_NoRecentSession"];
            TimelineText = _localizer["VoiceDiagnostics_TimelineUnavailable"];
            RecommendationText = snapshot.Permission.RequiresAuthorization
                ? _localizer["VoiceDiagnostics_RecommendationRequiresAuthorization"]
                : _localizer["VoiceDiagnostics_RecommendationNoRecentSession"];
            return;
        }

        SessionStatusText = ResolveSessionStatus(session);
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
}
