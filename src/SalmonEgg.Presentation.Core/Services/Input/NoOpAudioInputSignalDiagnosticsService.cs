using System.Threading;
using System.Threading.Tasks;

namespace SalmonEgg.Presentation.Core.Services.Input;

public sealed class NoOpAudioInputSignalDiagnosticsService : IAudioInputSignalDiagnosticsService
{
    public AudioInputSignalDiagnosticsSnapshot GetCurrentSnapshot()
        => AudioInputSignalDiagnosticsSnapshot.Unsupported;

    public Task StartMonitoringAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task StopMonitoringAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
