using System;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Infrastructure.Services;

public sealed class UnsupportedTerminalAuthenticationSessionFactory : ITerminalAuthenticationSessionFactory
{
    public bool IsSupported => false;

    public ValueTask<ITerminalAuthenticationSession> StartAsync(
        StdioInvocationSnapshot invocation,
        CancellationToken cancellationToken = default)
        => ValueTask.FromException<ITerminalAuthenticationSession>(
            new PlatformNotSupportedException("Interactive agent sign-in is not available on this platform."));
}
