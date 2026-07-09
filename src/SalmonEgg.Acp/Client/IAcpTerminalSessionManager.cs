using System;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Acp.Protocol;

namespace SalmonEgg.Acp.Client
{
    public interface IAcpTerminalSessionManager : IDisposable
    {
        Task<TerminalCreateResponse> CreateAsync(TerminalCreateRequest request, CancellationToken cancellationToken = default);

        Task<TerminalOutputResponse> GetOutputAsync(TerminalOutputRequest request, CancellationToken cancellationToken = default);

        Task<TerminalWaitForExitResponse> WaitForExitAsync(TerminalWaitForExitRequest request, CancellationToken cancellationToken = default);

        Task<TerminalKillResponse> KillAsync(TerminalKillRequest request, CancellationToken cancellationToken = default);

        Task<TerminalReleaseResponse> ReleaseAsync(TerminalReleaseRequest request, CancellationToken cancellationToken = default);
    }

    public sealed class UnsupportedAcpTerminalSessionManager : IAcpTerminalSessionManager
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
}
