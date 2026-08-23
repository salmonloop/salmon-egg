using System;
using System.Collections.Generic;
using System.Text;
using SalmonEgg.Domain.Interfaces.Transport;

namespace SalmonEgg.Infrastructure.Transport;

public sealed class UnsupportedStdioTransportFactory : IStdioTransportFactory
{
    private const string UnsupportedMessage =
        "Stdio transport requires a desktop process host and is not supported on this platform.";

    public ITransport Create(
        string command,
        string[] args,
        Encoding encoding,
        IReadOnlyDictionary<string, string>? environment = null)
        => throw new NotSupportedException(UnsupportedMessage);
}
