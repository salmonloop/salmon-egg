using System;
using SalmonEgg.Domain.Interfaces.Transport;
using SalmonEgg.Domain.Services;
using SalmonEgg.Acp.Client;
using SalmonEgg.Application.Services.Acp;

namespace SalmonEgg.Infrastructure.Client;

public sealed class AcpClientFactory : IAcpClientFactory
{
    private readonly IErrorLogger _errorLogger;
    private readonly ISessionManager _sessionManager;
    private readonly ITerminalSessionManager _terminalSessionManager;

    public AcpClientFactory(
        IErrorLogger errorLogger,
        ISessionManager sessionManager,
        ITerminalSessionManager terminalSessionManager)
    {
        _errorLogger = errorLogger ?? throw new ArgumentNullException(nameof(errorLogger));
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _terminalSessionManager = terminalSessionManager ?? throw new ArgumentNullException(nameof(terminalSessionManager));
    }

    public IAcpClient CreateClient(ITransport transport)
        => new SalmonEgg.Acp.Client.AcpClient(
            new DomainAcpTransportAdapter(transport ?? throw new ArgumentNullException(nameof(transport))),
            new DomainAcpClientLogger(_errorLogger),
            new DomainAcpClientSessionStore(_sessionManager),
            _terminalSessionManager);
}
