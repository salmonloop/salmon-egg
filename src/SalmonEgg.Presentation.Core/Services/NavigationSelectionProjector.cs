using System;
using System.Collections.Generic;
using SalmonEgg.Presentation.Models.Navigation;
using SalmonEgg.Presentation.ViewModels.Navigation;

namespace SalmonEgg.Presentation.Core.Services;

public sealed class NavigationSelectionProjector : INavigationSelectionProjector
{
    public NavigationViewProjection Project(
        NavigationSelectionState selection,
        StartNavItemViewModel startItem,
        DiscoverSessionsNavItemViewModel discoverSessionsItem,
        IReadOnlyDictionary<string, SessionNavItemViewModel> sessionIndex,
        IReadOnlyDictionary<string, ProjectNavItemViewModel> projectIndex)
    {
        var activeProjects = new HashSet<string>(StringComparer.Ordinal);
        var selectedSessions = new HashSet<string>(StringComparer.Ordinal);

        switch (selection)
        {
            case NavigationSelectionState.Start:
                return new NavigationViewProjection(
                    ControlSelectedItem: startItem,
                    IsSettingsSelected: false,
                    ActiveProjectIds: activeProjects,
                    SelectedSessionIds: selectedSessions);

            case NavigationSelectionState.DiscoverSessions:
                return new NavigationViewProjection(
                    ControlSelectedItem: discoverSessionsItem,
                    IsSettingsSelected: false,
                    ActiveProjectIds: activeProjects,
                    SelectedSessionIds: selectedSessions);

            case NavigationSelectionState.Settings:
                return new NavigationViewProjection(
                    ControlSelectedItem: null,
                    IsSettingsSelected: true,
                    ActiveProjectIds: activeProjects,
                    SelectedSessionIds: selectedSessions);

            case NavigationSelectionState.Session sessionSelection
                when !string.IsNullOrWhiteSpace(sessionSelection.SessionId)
                     && sessionIndex.TryGetValue(sessionSelection.SessionId, out var sessionItem):
                selectedSessions.Add(sessionItem.SessionId);

                if (projectIndex.TryGetValue(sessionItem.ProjectId, out var projectItem))
                {
                    activeProjects.Add(projectItem.ProjectId);

                    return new NavigationViewProjection(
                        ControlSelectedItem: sessionItem,
                        IsSettingsSelected: false,
                        ActiveProjectIds: activeProjects,
                        SelectedSessionIds: selectedSessions);
                }

                return new NavigationViewProjection(
                    ControlSelectedItem: sessionItem,
                    IsSettingsSelected: false,
                    ActiveProjectIds: activeProjects,
                    SelectedSessionIds: selectedSessions);

            case NavigationSelectionState.Session sessionSelection
                when !string.IsNullOrWhiteSpace(sessionSelection.SessionId):
                return new NavigationViewProjection(
                    ControlSelectedItem: null,
                    IsSettingsSelected: false,
                    ActiveProjectIds: activeProjects,
                    SelectedSessionIds: selectedSessions);

            default:
                return new NavigationViewProjection(
                    ControlSelectedItem: startItem,
                    IsSettingsSelected: false,
                    ActiveProjectIds: activeProjects,
                    SelectedSessionIds: selectedSessions);
        }
    }
}
