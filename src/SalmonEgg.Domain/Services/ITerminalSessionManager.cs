using System;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Acp.Protocol;

namespace SalmonEgg.Domain.Services
{
    /// <summary>
    /// Manages ACP terminal sessions owned by the client.
    /// </summary>
    public interface ITerminalSessionManager : IDisposable
    {
        Task<TerminalCreateResponse> CreateAsync(TerminalCreateRequest request, CancellationToken cancellationToken = default);

        Task<TerminalOutputResponse> GetOutputAsync(TerminalOutputRequest request, CancellationToken cancellationToken = default);

        Task<TerminalWaitForExitResponse> WaitForExitAsync(TerminalWaitForExitRequest request, CancellationToken cancellationToken = default);

        Task<TerminalKillResponse> KillAsync(TerminalKillRequest request, CancellationToken cancellationToken = default);

        Task<TerminalReleaseResponse> ReleaseAsync(TerminalReleaseRequest request, CancellationToken cancellationToken = default);
    }
}
