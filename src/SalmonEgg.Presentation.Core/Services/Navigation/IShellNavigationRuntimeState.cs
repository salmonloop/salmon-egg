using System.ComponentModel;
using SalmonEgg.Presentation.Models.Navigation;

namespace SalmonEgg.Presentation.Core.Services;

public interface IShellNavigationRuntimeState : INotifyPropertyChanged
{
    long LatestActivationToken { get; set; }

    SessionActivationSnapshot? ActiveSessionActivation { get; set; }

    long ActiveSessionActivationVersion { get; set; }

    long CommittedSessionActivationVersion { get; set; }

    string? DesiredSessionId { get; set; }

    string? CommittedSessionId { get; set; }

    bool IsSessionActivationInProgress { get; set; }

    ShellNavigationContent CurrentShellContent { get; set; }

    ShellNavigationContent? PendingShellContent { get; set; }
}
