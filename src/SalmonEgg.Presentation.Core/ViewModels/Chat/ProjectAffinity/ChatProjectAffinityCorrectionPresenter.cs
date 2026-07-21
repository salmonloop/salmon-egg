using System;
using System.Collections.Generic;
using Microsoft.Extensions.Localization;
using SalmonEgg.Domain.Models;
using SalmonEgg.Presentation.Core.Resources;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Services.ProjectAffinity;
using SalmonEgg.Presentation.ViewModels.Chat;

namespace SalmonEgg.Presentation.Core.ViewModels.Chat.ProjectAffinity;

public sealed class ChatProjectAffinityCorrectionPresenter
{
    private readonly IProjectAffinityResolver _resolver;
    private readonly IStringLocalizer<CoreStrings>? _localizer;

    public ChatProjectAffinityCorrectionPresenter(
        IProjectAffinityResolver resolver,
        IStringLocalizer<CoreStrings>? localizer = null)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _localizer = localizer;
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
            RemoteDirectories: input.RemoteDirectories,
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
            Message: ResolveMessage(resolution.Source),
            SelectedOverrideProjectId: selectedOverrideProjectId);
    }

    private string ResolveMessage(ProjectAffinitySource source)
        => source switch
        {
            ProjectAffinitySource.Override => Localize(
                "ChatProjectAffinity_OverrideMessage",
                "Local project override applied. You can clear it anytime."),
            ProjectAffinitySource.NeedsMapping => Localize(
                "ChatProjectAffinity_NeedsMappingMessage",
                "This remote session is not matched to a local project. Correct it manually."),
            _ => Localize(
                "ChatProjectAffinity_UnclassifiedMessage",
                "This session is unclassified. You can correct it manually.")
        };

    private string Localize(string key, string fallback)
    {
        if (_localizer is null)
        {
            return fallback;
        }

        var localized = _localizer[key];
        return localized.ResourceNotFound || string.IsNullOrWhiteSpace(localized.Value)
            ? fallback
            : localized.Value;
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
