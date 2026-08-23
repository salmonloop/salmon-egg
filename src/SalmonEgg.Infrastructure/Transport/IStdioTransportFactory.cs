using System.Collections.Generic;
using System.Text;
using SalmonEgg.Domain.Interfaces.Transport;

namespace SalmonEgg.Infrastructure.Transport;

public interface IStdioTransportFactory
{
    /// <summary>
    /// Creates a stdio transport. <paramref name="environment"/> entries are applied on top of the
    /// inherited process environment; null or empty leaves the environment untouched.
    /// </summary>
    ITransport Create(
        string command,
        string[] args,
        Encoding encoding,
        IReadOnlyDictionary<string, string>? environment = null);
}
