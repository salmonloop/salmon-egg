using System.Threading;
using System.Threading.Tasks;

namespace SalmonEgg.Presentation.Core.Services.Chat;

public interface IConversationActivationCoordinator
{
    Task<ConversationActivationResult> ActivateSessionAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<ConversationMutationResult> ArchiveConversationAsync(
        string conversationId,
        string? activeConversationId,
        CancellationToken cancellationToken = default);

    Task<ConversationMutationResult> DeleteConversationAsync(
        string conversationId,
        string? activeConversationId,
        CancellationToken cancellationToken = default);
}

public sealed record ConversationActivationResult(
    bool Succeeded,
    string? ConversationId,
    string? FailureReason);

public sealed record ConversationMutationResult(
    bool Succeeded,
    bool ClearedActiveConversation,
    string? FailureReason);
