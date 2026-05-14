using System;
using System.Text;
using SalmonEgg.Domain.Interfaces.Transport;

namespace SalmonEgg.Infrastructure.Transport;

public sealed class DesktopStdioTransportFactory : IStdioTransportFactory
{
    public ITransport Create(string command, string[] args, Encoding encoding)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            throw new ArgumentException("Stdio transport requires a command.", nameof(command));
        }

        return new StdioTransport(command, args, encoding);
    }
}
