using System.Threading.Tasks;
using SalmonEgg.Presentation.Models.Navigation;
using SalmonEgg.Presentation.Core.Services.Chat;

namespace SalmonEgg.Presentation.Core.Services;

public interface INavigationCoordinator
{
    Task<bool> ActivateStartAsync(string? projectIdForNewSession = null);

    Task ActivateDiscoverSessionsAsync();

    Task ActivateSettingsAsync(string settingsKey);

    Task<bool> ActivateSessionAsync(string sessionId, string? projectId);

    Task<DiscoverRemoteSessionOpenResult> ActivateDiscoveredRemoteSessionAsync(
        DiscoverRemoteSessionOpenRequest request);

    void SyncSelectionFromShellContent(ShellNavigationContent content);
}
