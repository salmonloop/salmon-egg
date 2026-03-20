using SalmonEgg.Presentation.Models.Navigation;

namespace SalmonEgg.Presentation.Core.Services;

public interface IShellSelectionStateStore : IShellSelectionReadModel
{
    void SetSelection(NavigationSelectionState selection);
}
