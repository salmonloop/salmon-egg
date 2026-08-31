using System;

namespace SalmonEgg.Presentation.Core.Services.ProjectAffinity;

/// <summary>
/// Resolves a conversation's activation project by combining its own affinity facts with the
/// authoritative navigation preferences.
/// </summary>
public sealed class ConversationProjectAffinityResolver : IConversationProjectAffinityResolver
{
    private readonly IProjectAffinityResolver _affinityResolver;
    private readonly INavigationProjectPreferences _preferences;

    public ConversationProjectAffinityResolver(
        IProjectAffinityResolver affinityResolver,
        INavigationProjectPreferences preferences)
    {
        _affinityResolver = affinityResolver ?? throw new ArgumentNullException(nameof(affinityResolver));
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
    }

    public string? ResolveActivationProjectId(ConversationProjectAffinityRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return _affinityResolver.Resolve(new ProjectAffinityRequest(
            RemoteCwd: request.Cwd,
            BoundProfileId: request.BoundProfileId,
            RemoteSessionId: request.RemoteSessionId,
            OverrideProjectId: request.OverrideProjectId,
            Projects: _preferences.Projects,
            RemoteDirectories: _preferences.AgentRemoteDirectories,
            UnclassifiedProjectId: NavigationProjectIds.Unclassified,
            NavigationRemoteDirectoryIds: _preferences.NavigationRemoteDirectoryIds)).EffectiveProjectId;
    }
}
