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

    /// <summary>
    /// Drops the activation fields that still refer to a conversation that no longer exists.
    /// </summary>
    /// <param name="sessionId">The conversation that was archived or deleted.</param>
    /// <returns>
    /// <see langword="true"/> when this conversation owned activation state, so the caller knows the
    /// activation it belonged to must be abandoned.
    /// </returns>
    /// <remarks>
    /// The activation fields describe one conversation between them, so retiring that conversation has
    /// to clear them as a set: clearing an id while leaving its version behind leaves the runtime
    /// describing something that is gone. Owning that invariant here keeps every caller from having to
    /// know which fields travel together.
    /// </remarks>
    bool TryRetireSession(string sessionId);
}
