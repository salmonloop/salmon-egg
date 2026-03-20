using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Models.Navigation;

namespace SalmonEgg.Presentation.Core.Services.Chat;

public sealed class ConversationCatalogFacade : IConversationCatalog, IDisposable
{
    private readonly ChatConversationWorkspace _workspace;
    private readonly IConversationActivationCoordinator _activationCoordinator;
    private readonly IShellSelectionReadModel _shellSelection;
    private readonly ILogger<ConversationCatalogFacade> _logger;
    private bool _disposed;

    public ConversationCatalogFacade(
        ChatConversationWorkspace workspace,
        IConversationActivationCoordinator activationCoordinator,
        IShellSelectionReadModel shellSelection,
        ILogger<ConversationCatalogFacade> logger)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _activationCoordinator = activationCoordinator ?? throw new ArgumentNullException(nameof(activationCoordinator));
        _shellSelection = shellSelection ?? throw new ArgumentNullException(nameof(shellSelection));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _workspace.PropertyChanged += OnWorkspacePropertyChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsConversationListLoading => _workspace.IsConversationListLoading;

    public int ConversationListVersion => _workspace.ConversationListVersion;

    public string[] GetKnownConversationIds() => _workspace.GetKnownConversationIds();

    public Task RestoreAsync(CancellationToken cancellationToken = default)
        => _workspace.RestoreAsync(cancellationToken);

    public void RenameConversation(string conversationId, string newDisplayName)
        => _workspace.RenameConversation(conversationId, newDisplayName);

    public void ArchiveConversation(string conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return;
        }

        _ = RunMutationAsync(() => _activationCoordinator.ArchiveConversationAsync(
            conversationId,
            GetActiveConversationId()));
    }

    public void DeleteConversation(string conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return;
        }

        _ = RunMutationAsync(() => _activationCoordinator.DeleteConversationAsync(
            conversationId,
            GetActiveConversationId()));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _workspace.PropertyChanged -= OnWorkspacePropertyChanged;
    }

    private void OnWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs e)
        => PropertyChanged?.Invoke(this, e);

    private string? GetActiveConversationId()
        => _shellSelection.CurrentSelection is NavigationSelectionState.Session session
            ? session.SessionId
            : null;

    private async Task RunMutationAsync(Func<Task<ConversationMutationResult>> mutation)
    {
        try
        {
            await mutation().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Conversation catalog mutation failed");
        }
    }
}
