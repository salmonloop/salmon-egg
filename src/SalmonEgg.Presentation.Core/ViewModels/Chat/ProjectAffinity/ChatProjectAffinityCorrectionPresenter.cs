using System;
using System.Collections.Generic;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.ProjectAffinity;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Services.ProjectAffinity;
using SalmonEgg.Presentation.ViewModels.Chat;

namespace SalmonEgg.Presentation.Core.ViewModels.Chat.ProjectAffinity;

public sealed class ChatProjectAffinityCorrectionPresenter
{
    private readonly IProjectAffinityResolver _resolver;

    public ChatProjectAffinityCorrectionPresenter(IProjectAffinityResolver resolver)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    }

    public ChatProjectAffinityCorrectionState Present(ChatProjectAffinityCorrectionInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var options = BuildOptions(input.Projects);
        if (string.IsNullOrWhiteSpace(input.ConversationId))
        {
            return new ChatProjectAffinityCorrectionState(
                options,
                IsVisible: false,
                HasOverride: false,
                EffectiveProjectId: null,
                EffectiveSource: ProjectAffinitySource.Unclassified,
                Message: string.Empty,
                SelectedOverrideProjectId: null);
        }

        var resolution = _resolver.Resolve(new ProjectAffinityRequest(
            RemoteCwd: input.RemoteCwd,
            BoundProfileId: input.BoundProfileId,
            RemoteSessionId: input.RemoteSessionId,
            OverrideProjectId: input.OverrideProjectId,
            Projects: input.Projects,
            PathMappings: input.PathMappings,
            UnclassifiedProjectId: NavigationProjectIds.Unclassified));

        var hasOverride = !string.IsNullOrWhiteSpace(input.OverrideProjectId);
        var selectedOverrideProjectId = hasOverride
            ? input.OverrideProjectId
            : ResolveSelectedOverrideProjectId(input.SelectedOverrideProjectId, options);
        var isRemoteBound = !string.IsNullOrWhiteSpace(input.RemoteSessionId)
            || !string.IsNullOrWhiteSpace(input.BoundProfileId);

        return new ChatProjectAffinityCorrectionState(
            options,
            IsVisible: isRemoteBound && resolution.Source is
                ProjectAffinitySource.NeedsMapping or
                ProjectAffinitySource.Unclassified or
                ProjectAffinitySource.Override,
            HasOverride: hasOverride,
            EffectiveProjectId: resolution.EffectiveProjectId,
            EffectiveSource: resolution.Source,
            Message: resolution.Source switch
            {
                ProjectAffinitySource.Override => "已应用本地项目覆盖，可随时清除。",
                ProjectAffinitySource.NeedsMapping => "远程会话未匹配到本地项目，请手动更正。",
                _ => "当前会话归类为“未归类”，可手动更正。"
            },
            SelectedOverrideProjectId: selectedOverrideProjectId);
    }

    private static IReadOnlyList<ProjectAffinityOverrideOptionViewModel> BuildOptions(
        IReadOnlyList<ProjectDefinition> projects)
    {
        var options = new List<ProjectAffinityOverrideOptionViewModel>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var project in projects)
        {
            if (project is null
                || string.IsNullOrWhiteSpace(project.ProjectId)
                || string.IsNullOrWhiteSpace(project.Name)
                || !seen.Add(project.ProjectId))
            {
                continue;
            }

            options.Add(new ProjectAffinityOverrideOptionViewModel(project.ProjectId, project.Name));
        }

        options.Sort((left, right) => string.Compare(left.DisplayName, right.DisplayName, StringComparison.Ordinal));
        return options;
    }

    private static string? ResolveSelectedOverrideProjectId(
        string? currentSelectedOverrideProjectId,
        IReadOnlyList<ProjectAffinityOverrideOptionViewModel> options)
    {
        if (string.IsNullOrWhiteSpace(currentSelectedOverrideProjectId))
        {
            return null;
        }

        foreach (var option in options)
        {
            if (string.Equals(option.ProjectId, currentSelectedOverrideProjectId, StringComparison.Ordinal))
            {
                return currentSelectedOverrideProjectId;
            }
        }

        return null;
    }
}
