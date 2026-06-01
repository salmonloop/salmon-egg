using System.Threading;
using System.Threading.Tasks;

namespace SalmonEgg.Presentation.Core.Services.Input;

public interface IVoiceInputDiagnosticsService
{
    Task<VoiceInputDiagnosticsSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);

    Task<bool> TryOpenAuthorizationSettingsAsync(CancellationToken cancellationToken = default);
}
