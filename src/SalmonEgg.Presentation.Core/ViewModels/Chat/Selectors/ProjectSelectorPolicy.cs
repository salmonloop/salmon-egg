using System;
using System.Collections.Generic;
using System.Linq;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.ViewModels.Start;

namespace SalmonEgg.Presentation.Core.ViewModels.Chat.Selectors;

public sealed record ProjectSelectorPolicyInput(
    string Identity,
    IReadOnlyList<StartProjectOptionViewModel> Projects,
    string? SelectedProjectId,
    bool PendingProjectIntentResolved,
    bool HasLegalFallback);

public sealed class ProjectSelectorPolicy
{
    public SelectorPolicyProjection Project(ProjectSelectorPolicyInput input)
    {
        var realItems = (input.Projects ?? Array.Empty<StartProjectOptionViewModel>())
            .Where(static project => !string.IsNullOrWhiteSpace(project.ProjectId))
            .Select(project => ComposerSelectorItemViewModel.Real(
                ComposerSelectorKind.Project,
                project.ProjectId,
                string.IsNullOrWhiteSpace(project.DisplayName) ? project.ProjectId : project.DisplayName,
                input.Identity))
            .ToArray();

        if (!input.PendingProjectIntentResolved && !input.HasLegalFallback)
        {
            var unresolved = ComposerSelectorItemViewModel.Placeholder(
                ComposerSelectorKind.Project,
                SelectorPlaceholderKind.Unresolved,
                "Project unavailable",
                input.Identity,
                blocksSubmit: true);

            return new SelectorPolicyProjection(
                realItems,
                input.SelectedProjectId,
                unresolved,
                ReplaceSelectionWithPlaceholder: true,
                DisableRealItems: false,
                SelectorEnabled: false);
        }

        if (realItems.Length == 0 && input.HasLegalFallback)
        {
            var fallback = ComposerSelectorItemViewModel.Placeholder(
                ComposerSelectorKind.Project,
                SelectorPlaceholderKind.Fallback,
                "Unclassified",
                input.Identity,
                semanticValue: NavigationProjectIds.Unclassified,
                blocksSubmit: false,
                isSelectable: true);

            return new SelectorPolicyProjection(
                realItems,
                NavigationProjectIds.Unclassified,
                fallback,
                ReplaceSelectionWithPlaceholder: true,
                DisableRealItems: false,
                SelectorEnabled: true);
        }

        return new SelectorPolicyProjection(
            realItems,
            string.IsNullOrWhiteSpace(input.SelectedProjectId)
                ? NavigationProjectIds.Unclassified
                : input.SelectedProjectId,
            Placeholder: null,
            ReplaceSelectionWithPlaceholder: false,
            DisableRealItems: false,
            SelectorEnabled: realItems.Length > 0);
    }
}
