using System;

namespace SalmonEgg.Presentation.Core.Services.Input;

public enum VoiceInputDiagnosticSessionOutcome
{
    Unknown = 0,
    StartRequested = 1,
    ReadyWithoutRecognition = 2,
    PartialResultReceived = 3,
    FinalResultReceived = 4,
    Failed = 5
}

public sealed record VoiceInputDiagnosticSession(
    string RequestId,
    VoiceInputDiagnosticSessionOutcome Outcome,
    DateTimeOffset? StartRequestedAt,
    DateTimeOffset? RecognizerReadyAt,
    DateTimeOffset? FirstPartialAt,
    DateTimeOffset? FinalResultAt,
    DateTimeOffset? StopRequestedAt,
    DateTimeOffset? EndedAt,
    DateTimeOffset? ErrorAt,
    string? ErrorCode,
    string? ErrorMessage,
    string? LanguageTag);

public sealed record VoiceInputDiagnosticsSnapshot(
    bool IsSupported,
    bool IsListening,
    string CurrentLanguageTag,
    VoiceInputPermissionResult Permission,
    string? LatestLogFilePath,
    DateTimeOffset? LatestLogTimestamp,
    VoiceInputDiagnosticSession? LatestSession);
