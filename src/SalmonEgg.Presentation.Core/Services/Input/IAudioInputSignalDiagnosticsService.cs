using System;
using System.Threading;
using System.Threading.Tasks;

namespace SalmonEgg.Presentation.Core.Services.Input;

public interface IAudioInputSignalDiagnosticsService
{
    AudioInputSignalDiagnosticsSnapshot GetCurrentSnapshot();

    Task StartMonitoringAsync(CancellationToken cancellationToken = default);

    Task StopMonitoringAsync(CancellationToken cancellationToken = default);
}

public sealed record AudioInputSignalDiagnosticsSnapshot(
    bool IsSupported,
    bool IsMonitoring,
    int ObservedSampleCount,
    int ObservedNonSilentSampleCount,
    double MaxPeakLevel,
    DateTimeOffset? FirstNonSilentSampleObservedAt,
    DateTimeOffset? LastNonSilentSampleObservedAt,
    string? FailureMessage)
{
    public static AudioInputSignalDiagnosticsSnapshot Unsupported { get; } = new(
        IsSupported: false,
        IsMonitoring: false,
        ObservedSampleCount: 0,
        ObservedNonSilentSampleCount: 0,
        MaxPeakLevel: 0,
        FirstNonSilentSampleObservedAt: null,
        LastNonSilentSampleObservedAt: null,
        FailureMessage: null);
}
