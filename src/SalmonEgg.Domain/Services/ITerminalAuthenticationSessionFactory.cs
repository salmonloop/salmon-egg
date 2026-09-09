using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models;

namespace SalmonEgg.Domain.Services;

/// <summary>Creates independent interactive sign-in processes with a reliable exit result.</summary>
public interface ITerminalAuthenticationSessionFactory
{
    bool IsSupported { get; }

    ValueTask<ITerminalAuthenticationSession> StartAsync(
        StdioInvocationSnapshot invocation,
        CancellationToken cancellationToken = default);
}

public interface ITerminalAuthenticationSession : IInteractiveTerminalSession
{
    /// <summary>Null means no trustworthy normal exit status was available.</summary>
    Task<int?> Completion { get; }
}
