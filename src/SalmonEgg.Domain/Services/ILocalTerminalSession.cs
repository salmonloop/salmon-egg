using System;
using System.Threading;
using System.Threading.Tasks;

namespace SalmonEgg.Domain.Services;

/// <summary>
/// Represents one reusable local interactive terminal session bound to a conversation.
/// </summary>
public interface ILocalTerminalSession : IAsyncDisposable
{
    string ConversationId { get; }

    string CurrentWorkingDirectory { get; }

    bool CanAcceptInput { get; }

    event EventHandler<string>? OutputReceived;

    event EventHandler? StateChanged;

    ValueTask WriteInputAsync(string input, CancellationToken cancellationToken = default);

    ValueTask ResizeAsync(int columns, int rows, CancellationToken cancellationToken = default);
}
