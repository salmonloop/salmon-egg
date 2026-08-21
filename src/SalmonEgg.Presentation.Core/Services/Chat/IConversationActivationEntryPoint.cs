using System.Threading.Tasks;

namespace SalmonEgg.Presentation.Core.Services.Chat;

/// <summary>
/// The navigation owner's session activation entry point, narrowed to what external callers need.
/// </summary>
/// <remarks>
/// Implemented by the navigation ViewModel. Routing through it — rather than through the navigation
/// coordinator directly — keeps activation failures on the same user-visible feedback path the
/// mini window and global search already use.
/// </remarks>
public interface IConversationActivationEntryPoint
{
    Task<bool> ActivateSessionAsync(string sessionId, string? projectId);
}
