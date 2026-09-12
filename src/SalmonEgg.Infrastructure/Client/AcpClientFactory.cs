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
    private readonly ITransportErrorMessageFormatter? _transportErrorMessageFormatter;
    private readonly AcpProtocolExperimentPolicy _protocolExperiment;

    public AcpClientFactory(
        IErrorLogger errorLogger,
        ISessionManager sessionManager,
        ITerminalSessionManager terminalSessionManager,
        ITransportErrorMessageFormatter? transportErrorMessageFormatter = null,
        AcpProtocolExperimentPolicy? protocolExperiment = null)
    {
        _errorLogger = errorLogger ?? throw new ArgumentNullException(nameof(errorLogger));
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _terminalSessionManager = terminalSessionManager ?? throw new ArgumentNullException(nameof(terminalSessionManager));
        _transportErrorMessageFormatter = transportErrorMessageFormatter;
        _protocolExperiment = protocolExperiment ?? new AcpProtocolExperimentPolicy();
    }

    public IAcpClient CreateClient(ITransport transport)
        => new SalmonEgg.Acp.Client.AcpClient(
            new DomainAcpTransportAdapter(
                transport ?? throw new ArgumentNullException(nameof(transport)),
                _transportErrorMessageFormatter),
            new DomainAcpClientLogger(_errorLogger),
            new DomainAcpClientSessionStore(_sessionManager),
            _terminalSessionManager,
            new AcpClientOptions
            {
                ExperimentalProtocolVersions = _protocolExperiment.EnableDraftV2 ? [SalmonEgg.Acp.Protocol.AcpProtocolVersion.V2] : []
            });
}
