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
        SettingsNavItemViewModel settingsItem,
        IReadOnlyDictionary<string, SessionNavItemViewModel> sessionIndex)
    {
        switch (selection)
        {
            case NavigationSelectionState.Start:
                return new NavigationViewProjection(startItem, IsSettingsSelected: false);

            case NavigationSelectionState.DiscoverSessions:
                return new NavigationViewProjection(discoverSessionsItem, IsSettingsSelected: false);

            case NavigationSelectionState.Settings:
                return new NavigationViewProjection(settingsItem, IsSettingsSelected: true);

            case NavigationSelectionState.Session sessionSelection
                when !string.IsNullOrWhiteSpace(sessionSelection.SessionId)
                     && sessionIndex.TryGetValue(sessionSelection.SessionId, out var sessionItem):
                return new NavigationViewProjection(sessionItem, IsSettingsSelected: false);

            default:
                return new NavigationViewProjection(ControlSelectedItem: null, IsSettingsSelected: false);
        }
    }
}
