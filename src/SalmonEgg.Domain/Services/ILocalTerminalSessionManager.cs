using System;
using System.Threading;
using System.Threading.Tasks;

namespace SalmonEgg.Domain.Services;

/// <summary>
/// Manages conversation-scoped local terminal session reuse and disposal.
/// </summary>
public interface ILocalTerminalSessionManager : IAsyncDisposable
{
    ValueTask<ILocalTerminalSession> GetOrCreateAsync(
        string conversationId,
        string preferredCwd,
        CancellationToken cancellationToken = default);

    ValueTask DisposeConversationAsync(
        string conversationId,
        CancellationToken cancellationToken = default);
}
