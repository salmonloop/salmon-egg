using System;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models.Protocol;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Infrastructure.Services;

public sealed class UnsupportedTerminalSessionManager : ITerminalSessionManager
{
    private const string UnsupportedMessage = "ACP terminal sessions require a desktop process host and are not supported on this platform.";

    public Task<TerminalCreateResponse> CreateAsync(TerminalCreateRequest request, CancellationToken cancellationToken = default)
        => throw new NotSupportedException(UnsupportedMessage);

    public Task<TerminalOutputResponse> GetOutputAsync(TerminalOutputRequest request, CancellationToken cancellationToken = default)
        => throw new NotSupportedException(UnsupportedMessage);

    public Task<TerminalWaitForExitResponse> WaitForExitAsync(TerminalWaitForExitRequest request, CancellationToken cancellationToken = default)
        => throw new NotSupportedException(UnsupportedMessage);

    public Task<TerminalKillResponse> KillAsync(TerminalKillRequest request, CancellationToken cancellationToken = default)
        => throw new NotSupportedException(UnsupportedMessage);

    public Task<TerminalReleaseResponse> ReleaseAsync(TerminalReleaseRequest request, CancellationToken cancellationToken = default)
        => throw new NotSupportedException(UnsupportedMessage);

    public void Dispose()
    {
    }
}
