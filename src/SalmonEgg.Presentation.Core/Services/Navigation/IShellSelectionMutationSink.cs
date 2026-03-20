using SalmonEgg.Presentation.Models.Navigation;

namespace SalmonEgg.Presentation.Core.Services;

public interface IShellSelectionMutationSink
{
    void SetSelection(NavigationSelectionState selection);
}
