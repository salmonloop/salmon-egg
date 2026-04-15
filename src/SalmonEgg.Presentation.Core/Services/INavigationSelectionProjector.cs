using System.Collections.Generic;
using SalmonEgg.Presentation.Models.Navigation;
using SalmonEgg.Presentation.ViewModels.Navigation;

namespace SalmonEgg.Presentation.Core.Services;

public interface INavigationSelectionProjector
{
    NavigationViewProjection Project(
        NavigationSelectionState selection,
        StartNavItemViewModel startItem,
        DiscoverSessionsNavItemViewModel discoverSessionsItem,
        IReadOnlyDictionary<string, SessionNavItemViewModel> sessionIndex,
        IReadOnlyDictionary<string, ProjectNavItemViewModel> projectIndex);
}
