using System;
using System.Text;
using SalmonEgg.Domain.Interfaces.Transport;

namespace SalmonEgg.Infrastructure.Transport;

public sealed class UnsupportedStdioTransportFactory : IStdioTransportFactory
{
    private const string UnsupportedMessage =
        "Stdio transport requires a desktop process host and is not supported on this platform.";

    public ITransport Create(string command, string[] args, Encoding encoding)
        => throw new NotSupportedException(UnsupportedMessage);
}
