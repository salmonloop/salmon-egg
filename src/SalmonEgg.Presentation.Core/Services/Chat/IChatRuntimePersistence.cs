using System.Threading;
using System.Threading.Tasks;

namespace SalmonEgg.Presentation.Core.Services.Chat;

/// <summary>
/// Durability surface of the chat runtime, symmetric to <see cref="IChatRuntimeInitialization"/>.
/// </summary>
/// <remarks>
/// Conversation state reaches disk through a debounce so that a burst of updates costs one write.
/// That trade is only sound while something eventually forces the pending write out, so the runtime
/// exposes the flush as an explicit contract rather than leaving it to disposal: a disposal-time
/// flush cannot be awaited, and dropping the pending write silently loses the newest turn.
/// </remarks>
public interface IChatRuntimePersistence
{
    /// <summary>
    /// Writes any state still held behind the persistence debounce.
    /// </summary>
    /// <remarks>
    /// Must be safe to call when nothing is pending, and must complete before the caller allows the
    /// process to exit.
    /// </remarks>
    Task FlushPendingStateAsync(CancellationToken cancellationToken = default);
}
