using SalmonEgg.Domain.Interfaces.Transport;
using SalmonEgg.Acp.Client;

namespace SalmonEgg.Domain.Services;

public interface IAcpClientFactory
{
    IAcpClient CreateClient(ITransport transport);
}
