using System.Text;
using SalmonEgg.Domain.Interfaces.Transport;

namespace SalmonEgg.Infrastructure.Transport;

public interface IStdioTransportFactory
{
    ITransport Create(string command, string[] args, Encoding encoding);
}
