using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models.Conversation;

namespace SalmonEgg.Domain.Services;

public interface IConversationStore
{
    Task<ConversationDocument> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(ConversationDocument document, CancellationToken cancellationToken = default);
}

