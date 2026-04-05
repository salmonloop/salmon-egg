using System.Threading;
using System.Threading.Tasks;

namespace SalmonEgg.Presentation.Core.Services.Chat;

/// <summary>
/// ACP session-update buffering contract used by hydration flows.
/// Keeps replay buffering/drain lifecycle authoritative in the transport adapter layer
/// instead of duplicating that state in the ViewModel.
/// </summary>
public interface IAcpSessionUpdateBufferController
{
    long BeginHydrationBufferingScope(string? sessionId);

    void SuppressBufferedUpdates(long hydrationAttemptId, string? reason = null);

    bool TryMarkHydrated(long hydrationAttemptId, bool lowTrust = false, string? reason = null);

    Task WaitForBufferedUpdatesDrainedAsync(long hydrationAttemptId, CancellationToken cancellationToken = default);
}
