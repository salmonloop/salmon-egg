using System.Threading;
using System.Threading.Tasks;

namespace SalmonEgg.Presentation.Core.Services.Input;

public interface IVoiceInputRuntimeDiagnosticsSource
{
    Task<VoiceInputRuntimeDiagnostics> GetRuntimeDiagnosticsAsync(CancellationToken cancellationToken = default);
}

public sealed record VoiceInputRuntimeDiagnostics(
    string? DefaultInputDeviceName,
    string? DefaultInputDeviceId,
    VoiceInputDiagnosticSession? LatestSession);
