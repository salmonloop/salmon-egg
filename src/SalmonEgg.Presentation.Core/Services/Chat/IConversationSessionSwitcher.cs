using System.Threading;
using System.Threading.Tasks;

namespace SalmonEgg.Presentation.Core.Services.Chat;

/// <summary>
/// Shell-facing conversation switch surface.
/// Implementations must complete local activation before returning so shell selection can remain stable.
/// For remote-bound conversations, remote SSOT hydration may continue asynchronously after the local switch succeeds.
/// </summary>
public interface IConversationSessionSwitcher
{
    Task<bool> SwitchConversationAsync(string conversationId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Optional shell-facing preview surface for priming the chat loading state before ChatView becomes visible.
/// This keeps first-frame UX smooth without committing conversation state ahead of SSOT activation.
/// </summary>
public interface IConversationActivationPreview
{
    void PrimeSessionSwitchPreview(string conversationId);

    void ClearSessionSwitchPreview(string conversationId);
}
