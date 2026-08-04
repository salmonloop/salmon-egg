using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;

namespace SalmonEgg.Presentation.Core.Services.Chat;

public interface IConversationCatalog : INotifyPropertyChanged
{
    bool IsConversationListLoading { get; }

    int ConversationListVersion { get; }

    string[] GetKnownConversationIds();

    Task RestoreAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes conversation state still held behind the save debounce.
    /// </summary>
    /// <remarks>
    /// The catalog coalesces rapid updates into a single write. Callers that are about to end the
    /// process must force the pending write out first, otherwise the newest turn is lost.
    /// </remarks>
    Task FlushPendingSaveAsync(CancellationToken cancellationToken = default);

    Task<ConversationMutationResult> ArchiveConversationAsync(string conversationId, CancellationToken cancellationToken = default);

    Task<ConversationMutationResult> DeleteConversationAsync(string conversationId, CancellationToken cancellationToken = default);
}
