using System;
using System.Threading;
using System.Threading.Tasks;

namespace SalmonEgg.Domain.Services;

/// <summary>The native terminal I/O surface shared by local shells and sign-in processes.</summary>
public interface IInteractiveTerminalSession : IAsyncDisposable
{
    LocalTerminalTransportMode TransportMode { get; }

    bool CanAcceptInput { get; }

    event EventHandler<string>? OutputReceived;

    event EventHandler? StateChanged;

    ValueTask WriteInputAsync(string input, CancellationToken cancellationToken = default);

    ValueTask ResizeAsync(int columns, int rows, CancellationToken cancellationToken = default);
}
