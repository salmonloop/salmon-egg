using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models.Diagnostics;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Presentation.Core.Services.Input;

public sealed class VoiceInputDiagnosticsService : IVoiceInputDiagnosticsService
{
    private const int MaxLogTailChars = 64000;

    private readonly IVoiceInputService _voiceInputService;
    private readonly IVoiceInputRuntimeDiagnosticsSource _runtimeDiagnosticsSource;
    private readonly IAppDataService _paths;
    private readonly ILogFileCatalog _logFileCatalog;

    public VoiceInputDiagnosticsService(
        IVoiceInputService voiceInputService,
        IVoiceInputRuntimeDiagnosticsSource runtimeDiagnosticsSource,
        IAppDataService paths,
        ILogFileCatalog logFileCatalog)
    {
        _voiceInputService = voiceInputService ?? throw new ArgumentNullException(nameof(voiceInputService));
        _runtimeDiagnosticsSource = runtimeDiagnosticsSource ?? throw new ArgumentNullException(nameof(runtimeDiagnosticsSource));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _logFileCatalog = logFileCatalog ?? throw new ArgumentNullException(nameof(logFileCatalog));
    }

    public async Task<VoiceInputDiagnosticsSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var permission = _voiceInputService.IsSupported
            ? await _voiceInputService.EnsurePermissionAsync(cancellationToken).ConfigureAwait(false)
            : new VoiceInputPermissionResult(
                VoiceInputPermissionStatus.Unsupported,
                "Voice input is not supported on this platform.");
        var runtimeDiagnostics = await _runtimeDiagnosticsSource.GetRuntimeDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
        var latestLog = await _logFileCatalog.GetLatestAsync(_paths.LogsDirectoryPath, cancellationToken).ConfigureAwait(false);
        VoiceInputDiagnosticSession? latestSession = null;
        if (latestLog is not null)
        {
            var logText = await _logFileCatalog.ReadTailAsync(latestLog.Path, MaxLogTailChars, cancellationToken).ConfigureAwait(false);
            latestSession = ParseLatestSession(logText);
        }
        latestSession = MergeLatestSession(latestSession, runtimeDiagnostics.LatestSession);

        return new VoiceInputDiagnosticsSnapshot(
            IsSupported: _voiceInputService.IsSupported,
            IsListening: _voiceInputService.IsListening,
            CurrentLanguageTag: CultureInfo.CurrentUICulture.Name,
            Permission: permission,
            DefaultInputDeviceName: runtimeDiagnostics.DefaultInputDeviceName,
            DefaultInputDeviceId: runtimeDiagnostics.DefaultInputDeviceId,
            LatestLogFilePath: latestLog?.Path,
            LatestLogTimestamp: latestLog?.LastWriteTimeUtc,
            LatestSession: latestSession);
    }

    public Task<bool> TryOpenAuthorizationSettingsAsync(CancellationToken cancellationToken = default)
        => _voiceInputService.TryRequestAuthorizationHelpAsync(cancellationToken);

    private static VoiceInputDiagnosticSession? ParseLatestSession(string? logText)
    {
        if (string.IsNullOrWhiteSpace(logText))
        {
            return null;
        }

        var sessions = new Dictionary<string, MutableVoiceInputSession>(StringComparer.Ordinal);
        foreach (var rawLine in SplitLines(logText))
        {
            if (string.IsNullOrWhiteSpace(rawLine)
                || rawLine.IndexOf("Voice input", StringComparison.Ordinal) < 0)
            {
                continue;
            }

            var requestId = ExtractTokenValue(rawLine, "RequestId=");
            if (string.IsNullOrWhiteSpace(requestId))
            {
                continue;
            }

            var timestamp = ParseTimestamp(rawLine);
            if (!sessions.TryGetValue(requestId, out var session))
            {
                session = new MutableVoiceInputSession(requestId);
                sessions.Add(requestId, session);
            }

            session.RegisterEvent(timestamp);
            if (rawLine.Contains("Voice input start requested.", StringComparison.Ordinal))
            {
                session.StartRequestedAt ??= timestamp;
                session.LanguageTag ??= ExtractTokenValue(rawLine, "LanguageTag=");
                continue;
            }

            if (rawLine.Contains("Voice input recognizer ready.", StringComparison.Ordinal))
            {
                session.RecognizerReadyAt ??= timestamp;
                session.LanguageTag ??= ExtractTokenValue(rawLine, "LanguageTag=");
                continue;
            }

            if (rawLine.Contains("Voice input first partial received.", StringComparison.Ordinal))
            {
                session.FirstPartialAt ??= timestamp;
                session.PartialResultCount = Math.Max(session.PartialResultCount, 1);
                continue;
            }

            if (rawLine.Contains("Voice input final result received.", StringComparison.Ordinal)
                || rawLine.Contains("Voice input first final result received.", StringComparison.Ordinal))
            {
                session.FinalResultAt ??= timestamp;
                session.FinalResultCount = Math.Max(session.FinalResultCount, 1);
                continue;
            }

            if (rawLine.Contains("Voice input stop requested.", StringComparison.Ordinal))
            {
                session.StopRequestedAt ??= timestamp;
                continue;
            }

            if (rawLine.Contains("Voice input session ended.", StringComparison.Ordinal))
            {
                session.EndedAt ??= timestamp;
                continue;
            }

            if (rawLine.Contains("Voice input session summary.", StringComparison.Ordinal))
            {
                session.PartialResultCount = ParseIntTokenValue(rawLine, "PartialCount=");
                session.FinalResultCount = ParseIntTokenValue(rawLine, "FinalCount=");
                session.EmptyPartialResultCount = ParseIntTokenValue(rawLine, "EmptyPartialCount=");
                session.EmptyFinalResultCount = ParseIntTokenValue(rawLine, "EmptyFinalCount=");
                session.CompletionStatus ??= ExtractTokenValue(rawLine, "CompletionStatus=");
                continue;
            }

            if (rawLine.Contains("Voice input session error.", StringComparison.Ordinal))
            {
                session.ErrorAt ??= timestamp;
                session.ErrorCode ??= ExtractTokenValue(rawLine, "ErrorCode=");
                session.ErrorMessage ??= ExtractMessageValue(rawLine, "Message=");
            }
        }

        MutableVoiceInputSession? latest = null;
        foreach (var session in sessions.Values)
        {
            if (latest is null
                || session.LatestEventAt > latest.LatestEventAt)
            {
                latest = session;
            }
        }

        return latest?.ToImmutable();
    }

    private static IEnumerable<string> SplitLines(string text)
    {
        return text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    private static DateTimeOffset? ParseTimestamp(string line)
    {
        var levelIndex = line.IndexOf(" [", StringComparison.Ordinal);
        if (levelIndex <= 0)
        {
            return null;
        }

        var prefix = line[..levelIndex];
        return DateTimeOffset.TryParseExact(
            prefix,
            "yyyy-MM-dd HH:mm:ss.fff zzz",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var timestamp)
            ? timestamp
            : null;
    }

    private static string? ExtractTokenValue(string line, string token)
    {
        var start = line.IndexOf(token, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        start += token.Length;
        if (start >= line.Length)
        {
            return null;
        }

        var end = line.IndexOf(' ', start);
        return end < 0
            ? line[start..]
            : line[start..end];
    }

    private static string? ExtractMessageValue(string line, string token)
    {
        var start = line.IndexOf(token, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        start += token.Length;
        return start >= line.Length ? null : line[start..].Trim();
    }

    private static int ParseIntTokenValue(string line, string token)
    {
        var value = ExtractTokenValue(line, token);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;
    }

    private static VoiceInputDiagnosticSession? MergeLatestSession(
        VoiceInputDiagnosticSession? parsedLogSession,
        VoiceInputDiagnosticSession? runtimeSession)
    {
        if (parsedLogSession is null)
        {
            return runtimeSession;
        }

        if (runtimeSession is null)
        {
            return parsedLogSession;
        }

        if (string.Equals(parsedLogSession.RequestId, runtimeSession.RequestId, StringComparison.Ordinal))
        {
            return parsedLogSession with
            {
                Outcome = runtimeSession.Outcome,
                StartRequestedAt = runtimeSession.StartRequestedAt ?? parsedLogSession.StartRequestedAt,
                RecognizerReadyAt = runtimeSession.RecognizerReadyAt ?? parsedLogSession.RecognizerReadyAt,
                FirstPartialAt = runtimeSession.FirstPartialAt ?? parsedLogSession.FirstPartialAt,
                FinalResultAt = runtimeSession.FinalResultAt ?? parsedLogSession.FinalResultAt,
                StopRequestedAt = runtimeSession.StopRequestedAt ?? parsedLogSession.StopRequestedAt,
                EndedAt = runtimeSession.EndedAt ?? parsedLogSession.EndedAt,
                ErrorAt = runtimeSession.ErrorAt ?? parsedLogSession.ErrorAt,
                ErrorCode = runtimeSession.ErrorCode ?? parsedLogSession.ErrorCode,
                ErrorMessage = runtimeSession.ErrorMessage ?? parsedLogSession.ErrorMessage,
                LanguageTag = runtimeSession.LanguageTag ?? parsedLogSession.LanguageTag,
                PartialResultCount = runtimeSession.PartialResultCount,
                FinalResultCount = runtimeSession.FinalResultCount,
                EmptyPartialResultCount = runtimeSession.EmptyPartialResultCount,
                EmptyFinalResultCount = runtimeSession.EmptyFinalResultCount,
                CompletionStatus = runtimeSession.CompletionStatus ?? parsedLogSession.CompletionStatus
            };
        }

        return GetLatestEventTimestamp(runtimeSession) >= GetLatestEventTimestamp(parsedLogSession)
            ? runtimeSession
            : parsedLogSession;
    }

    private static DateTimeOffset GetLatestEventTimestamp(VoiceInputDiagnosticSession session)
        => session.EndedAt
            ?? session.ErrorAt
            ?? session.StopRequestedAt
            ?? session.FinalResultAt
            ?? session.FirstPartialAt
            ?? session.RecognizerReadyAt
            ?? session.StartRequestedAt
            ?? DateTimeOffset.MinValue;

    private sealed class MutableVoiceInputSession
    {
        public MutableVoiceInputSession(string requestId)
        {
            RequestId = requestId;
        }

        public string RequestId { get; }

        public DateTimeOffset? StartRequestedAt { get; set; }

        public DateTimeOffset? RecognizerReadyAt { get; set; }

        public DateTimeOffset? FirstPartialAt { get; set; }

        public DateTimeOffset? FinalResultAt { get; set; }

        public DateTimeOffset? StopRequestedAt { get; set; }

        public DateTimeOffset? EndedAt { get; set; }

        public DateTimeOffset? ErrorAt { get; set; }

        public string? ErrorCode { get; set; }

        public string? ErrorMessage { get; set; }

        public string? LanguageTag { get; set; }

        public int PartialResultCount { get; set; }

        public int FinalResultCount { get; set; }

        public int EmptyPartialResultCount { get; set; }

        public int EmptyFinalResultCount { get; set; }

        public string? CompletionStatus { get; set; }

        public DateTimeOffset LatestEventAt { get; private set; } = DateTimeOffset.MinValue;

        public void RegisterEvent(DateTimeOffset? timestamp)
        {
            if (timestamp is not null && timestamp.Value > LatestEventAt)
            {
                LatestEventAt = timestamp.Value;
            }
        }

        public VoiceInputDiagnosticSession ToImmutable()
            => new(
                RequestId,
                DetermineOutcome(),
                StartRequestedAt,
                RecognizerReadyAt,
                FirstPartialAt,
                FinalResultAt,
                StopRequestedAt,
                EndedAt,
                ErrorAt,
                ErrorCode,
                ErrorMessage,
                LanguageTag,
                PartialResultCount,
                FinalResultCount,
                EmptyPartialResultCount,
                EmptyFinalResultCount,
                CompletionStatus);

        private VoiceInputDiagnosticSessionOutcome DetermineOutcome()
        {
            if (ErrorAt is not null)
            {
                return VoiceInputDiagnosticSessionOutcome.Failed;
            }

            if (FinalResultAt is not null)
            {
                return VoiceInputDiagnosticSessionOutcome.FinalResultReceived;
            }

            if (FirstPartialAt is not null)
            {
                return VoiceInputDiagnosticSessionOutcome.PartialResultReceived;
            }

            if (RecognizerReadyAt is not null)
            {
                return VoiceInputDiagnosticSessionOutcome.ReadyWithoutRecognition;
            }

            if (StartRequestedAt is not null)
            {
                return VoiceInputDiagnosticSessionOutcome.StartRequested;
            }

            return VoiceInputDiagnosticSessionOutcome.Unknown;
        }
    }
}
