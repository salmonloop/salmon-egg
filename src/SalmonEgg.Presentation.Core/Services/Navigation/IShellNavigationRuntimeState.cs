using SalmonEgg.Presentation.Models.Navigation;

namespace SalmonEgg.Presentation.Core.Services;

public interface IShellNavigationRuntimeState
{
    long LatestActivationToken { get; set; }

    long ActiveSessionActivationVersion { get; set; }

    long CommittedSessionActivationVersion { get; set; }

    string? DesiredSessionId { get; set; }

    string? CommittedSessionId { get; set; }

    bool IsSessionActivationInProgress { get; set; }

    ShellNavigationContent CurrentShellContent { get; set; }
}
