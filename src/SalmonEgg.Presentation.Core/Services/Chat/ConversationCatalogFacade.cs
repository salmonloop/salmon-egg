using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Models.Navigation;

namespace SalmonEgg.Presentation.Core.Services.Chat;

public sealed class ConversationCatalogFacade : IConversationCatalog, IDisposable
{
    private readonly ChatConversationWorkspace _workspace;
    private readonly INavigationProjectPreferences _projectPreferences;
    private readonly IConversationActivationCoordinator _activationCoordinator;
    private readonly IShellSelectionReadModel _shellSelection;
    private readonly INavigationCoordinator _navigationCoordinator;
    private readonly ILogger<ConversationCatalogFacade> _logger;
    private bool _disposed;

    public ConversationCatalogFacade(
        ChatConversationWorkspace workspace,
        INavigationProjectPreferences projectPreferences,
        IConversationActivationCoordinator activationCoordinator,
        IShellSelectionReadModel shellSelection,
        INavigationCoordinator navigationCoordinator,
        ILogger<ConversationCatalogFacade> logger)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _projectPreferences = projectPreferences ?? throw new ArgumentNullException(nameof(projectPreferences));
        _activationCoordinator = activationCoordinator ?? throw new ArgumentNullException(nameof(activationCoordinator));
        _shellSelection = shellSelection ?? throw new ArgumentNullException(nameof(shellSelection));
        _navigationCoordinator = navigationCoordinator ?? throw new ArgumentNullException(nameof(navigationCoordinator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _workspace.PropertyChanged += OnWorkspacePropertyChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsConversationListLoading => _workspace.IsConversationListLoading;

    public int ConversationListVersion => _workspace.ConversationListVersion;

    public string[] GetKnownConversationIds() => _workspace.GetKnownConversationIds();

    public IReadOnlyList<ConversationProjectTargetOption> GetConversationProjectTargets()
    {
        var options = new List<ConversationProjectTargetOption>
        {
            new(NavigationProjectIds.Unclassified, "未归类")
        };
        var seen = new HashSet<string>(StringComparer.Ordinal)
        {
            NavigationProjectIds.Unclassified
        };

        foreach (var project in _projectPreferences.Projects
                     .Where(project => project != null
                         && !string.IsNullOrWhiteSpace(project.ProjectId)
                         && !string.IsNullOrWhiteSpace(project.Name))
                     .OrderBy(project => project.Name, StringComparer.Ordinal))
        {
            if (!seen.Add(project.ProjectId))
            {
                continue;
            }

            options.Add(new ConversationProjectTargetOption(project.ProjectId, project.Name));
        }

        return options;
    }

    public Task RestoreAsync(CancellationToken cancellationToken = default)
        => _workspace.RestoreAsync(cancellationToken);

    public async Task RegisterConversationAsync(
        string conversationId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return;
        }

        var timestamp = DateTime.UtcNow;
        await _workspace
            .RegisterConversationAsync(conversationId, timestamp, timestamp, cancellationToken)
            .ConfigureAwait(false);
        _workspace.ScheduleSave();
    }

    public void RenameConversation(string conversationId, string newDisplayName)
        => _workspace.RenameConversation(conversationId, newDisplayName);

    public void MoveConversationToProject(string conversationId, string projectId)
        => _workspace.MoveConversationToProject(conversationId, projectId);

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

    public Task RegisterConversationAsync(string conversationId)
    {
        return RegisterConversationAsync(conversationId, CancellationToken.None);
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
            var result = await mutation().ConfigureAwait(true);
            if (result.Succeeded && result.ClearedActiveConversation)
            {
                await _navigationCoordinator.ActivateStartAsync().ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Conversation catalog mutation failed");
        }
    }
}
