using System.Threading;
using System.Threading.Tasks;

namespace SalmonEgg.Presentation.Core.Services.Chat;

/// <summary>
/// Shell-facing conversation switch surface.
/// Implementations must complete the local selection commit before returning so shell selection can remain stable.
/// For remote-bound conversations, remote SSOT hydration may continue asynchronously after the local switch succeeds.
/// Late completion from superseded background hydration must not take back ownership from the newest activation intent.
/// </summary>
public interface IConversationSessionSwitcher
{
    Task<bool> SwitchConversationAsync(string conversationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers a discovered remote session as a local conversation and records its remote binding.
    /// Shell activation remains owned by the navigation coordinator activation path.
    /// </summary>
    Task<DiscoverRemoteSessionOpenResult> OpenDiscoveredRemoteSessionAsync(
        DiscoverRemoteSessionOpenRequest request,
        CancellationToken cancellationToken = default);

    Task DiscardDiscoveredRemoteSessionAsync(
        string localConversationId,
        CancellationToken cancellationToken = default);
}
