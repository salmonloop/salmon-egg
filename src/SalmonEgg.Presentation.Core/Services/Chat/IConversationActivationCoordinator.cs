using System.Threading;
using System.Threading.Tasks;

namespace SalmonEgg.Presentation.Core.Services.Chat;

public enum ConversationActivationHydrationMode
{
    WorkspaceSnapshot = 0,
    SelectionOnly = 1,
    MetadataOnly = 2
}

public interface IConversationActivationCoordinator
{
    Task<ConversationActivationResult> ActivateSessionAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<ConversationActivationResult> ActivateSessionAsync(
        string sessionId,
        ConversationActivationHydrationMode hydrationMode,
        CancellationToken cancellationToken = default);

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
