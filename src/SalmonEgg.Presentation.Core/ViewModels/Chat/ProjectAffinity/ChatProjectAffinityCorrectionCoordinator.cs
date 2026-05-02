using System;
using System.Linq;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.ProjectAffinity;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Core.Services.ProjectAffinity;

namespace SalmonEgg.Presentation.Core.ViewModels.Chat.ProjectAffinity;

public sealed class ChatProjectAffinityCorrectionCoordinator
{
    private readonly ChatProjectAffinityCorrectionPresenter _presenter;

    public ChatProjectAffinityCorrectionCoordinator(IProjectAffinityResolver resolver)
    {
        _presenter = new ChatProjectAffinityCorrectionPresenter(resolver);
    }

    public ChatProjectAffinityCorrectionState Present(
        ChatConversationWorkspace conversationWorkspace,
        ISessionManager sessionManager,
        string? requestedConversationId,
        string? currentConversationId,
        string? currentRemoteSessionId,
        string? selectedProfileId,
        string? selectedOverrideProjectId,
        IReadOnlyList<ProjectDefinition> projects,
        IReadOnlyList<ProjectPathMapping> pathMappings)
    {
        ArgumentNullException.ThrowIfNull(conversationWorkspace);
        ArgumentNullException.ThrowIfNull(sessionManager);
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(pathMappings);

        var activeConversationId = string.IsNullOrWhiteSpace(requestedConversationId)
            ? currentConversationId
            : requestedConversationId;
        var binding = string.IsNullOrWhiteSpace(activeConversationId)
            ? null
            : conversationWorkspace.GetRemoteBinding(activeConversationId);
        var remoteSessionId = binding?.RemoteSessionId;
        var boundProfileId = binding?.BoundProfileId;
        if (!string.IsNullOrWhiteSpace(activeConversationId)
            && string.Equals(activeConversationId, currentConversationId, StringComparison.Ordinal))
        {
            remoteSessionId ??= currentRemoteSessionId;
            boundProfileId ??= selectedProfileId;
        }

        var overrideProjectId = string.IsNullOrWhiteSpace(activeConversationId)
            ? null
            : conversationWorkspace.GetProjectAffinityOverride(activeConversationId)?.ProjectId;
        var remoteCwd = string.IsNullOrWhiteSpace(activeConversationId)
            ? null
            : sessionManager.GetSession(activeConversationId)?.Cwd;

        return _presenter.Present(new ChatProjectAffinityCorrectionInput(
            ConversationId: activeConversationId,
            RemoteSessionId: remoteSessionId,
            BoundProfileId: boundProfileId,
            RemoteCwd: remoteCwd,
            OverrideProjectId: overrideProjectId,
            SelectedOverrideProjectId: selectedOverrideProjectId,
            Projects: projects.ToArray(),
            PathMappings: pathMappings.ToArray()));
    }
}
