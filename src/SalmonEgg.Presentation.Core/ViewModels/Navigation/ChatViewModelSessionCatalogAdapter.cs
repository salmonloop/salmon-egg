using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Presentation.Core.Services.Chat;

namespace SalmonEgg.Presentation.ViewModels.Navigation;

public sealed class ChatViewModelSessionCatalogAdapter : IChatSessionCatalog
{
    private readonly IConversationCatalog _conversationCatalog;

    public ChatViewModelSessionCatalogAdapter(IConversationCatalog conversationCatalog)
    {
        _conversationCatalog = conversationCatalog ?? throw new ArgumentNullException(nameof(conversationCatalog));
    }

    public bool IsConversationListLoading => _conversationCatalog.IsConversationListLoading;

    public int ConversationListVersion => _conversationCatalog.ConversationListVersion;

    public event PropertyChangedEventHandler? PropertyChanged
    {
        add => _conversationCatalog.PropertyChanged += value;
        remove => _conversationCatalog.PropertyChanged -= value;
    }

    public string[] GetKnownConversationIds() => _conversationCatalog.GetKnownConversationIds();

    public IReadOnlyList<ConversationProjectTargetOption> GetConversationProjectTargets()
        => _conversationCatalog.GetConversationProjectTargets();

    public Task RestoreAsync(CancellationToken cancellationToken = default)
        => _conversationCatalog.RestoreAsync(cancellationToken);

    public void RenameConversation(string conversationId, string newName)
        => _conversationCatalog.RenameConversation(conversationId, newName);

    public void MoveConversationToProject(string conversationId, string projectId)
        => _conversationCatalog.MoveConversationToProject(conversationId, projectId);

    public Task<ConversationMutationResult> ArchiveConversationAsync(string conversationId, CancellationToken cancellationToken = default)
        => _conversationCatalog.ArchiveConversationAsync(conversationId, cancellationToken);

    public Task<ConversationMutationResult> DeleteConversationAsync(string conversationId, CancellationToken cancellationToken = default)
        => _conversationCatalog.DeleteConversationAsync(conversationId, cancellationToken);
}
