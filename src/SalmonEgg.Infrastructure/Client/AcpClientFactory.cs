using System;
using SalmonEgg.Domain.Interfaces;
using SalmonEgg.Domain.Interfaces.Transport;
using SalmonEgg.Domain.Services;
using SalmonEgg.Infrastructure.Serialization;

namespace SalmonEgg.Infrastructure.Client;

public sealed class AcpClientFactory : IAcpClientFactory
{
    private readonly IMessageParser _messageParser;
    private readonly IMessageValidator _messageValidator;
    private readonly IErrorLogger _errorLogger;
    private readonly ISessionManager _sessionManager;
    private readonly ITerminalSessionManager _terminalSessionManager;

    public AcpClientFactory(
        IMessageParser messageParser,
        IMessageValidator messageValidator,
        IErrorLogger errorLogger,
        ISessionManager sessionManager,
        ITerminalSessionManager terminalSessionManager)
    {
        _messageParser = messageParser ?? throw new ArgumentNullException(nameof(messageParser));
        _messageValidator = messageValidator ?? throw new ArgumentNullException(nameof(messageValidator));
        _errorLogger = errorLogger ?? throw new ArgumentNullException(nameof(errorLogger));
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _terminalSessionManager = terminalSessionManager ?? throw new ArgumentNullException(nameof(terminalSessionManager));
    }

    public IAcpClient CreateClient(ITransport transport)
        => new AcpClient(
            transport ?? throw new ArgumentNullException(nameof(transport)),
            _messageParser,
            _messageValidator,
            _errorLogger,
            _sessionManager,
            _terminalSessionManager);
}
