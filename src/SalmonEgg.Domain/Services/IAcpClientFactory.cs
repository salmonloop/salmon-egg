using SalmonEgg.Domain.Interfaces.Transport;

namespace SalmonEgg.Domain.Services;

public interface IAcpClientFactory
{
    IAcpClient CreateClient(ITransport transport);
}
