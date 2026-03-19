using System.Threading.Tasks;
using SalmonEgg.Presentation.Models.Navigation;

namespace SalmonEgg.Presentation.Core.Services;

public interface INavigationCoordinator
{
    Task ActivateStartAsync();

    Task ActivateSettingsAsync(string settingsKey);

    Task ActivateSessionAsync(string sessionId, string? projectId);

    Task ToggleProjectAsync(string projectId);

    void SyncSelectionFromShellContent(ShellNavigationContent content, string? currentSessionId);
}
