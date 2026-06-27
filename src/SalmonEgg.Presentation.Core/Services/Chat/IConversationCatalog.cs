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

    Task<ConversationMutationResult> ArchiveConversationAsync(string conversationId, CancellationToken cancellationToken = default);

    Task<ConversationMutationResult> DeleteConversationAsync(string conversationId, CancellationToken cancellationToken = default);
}
