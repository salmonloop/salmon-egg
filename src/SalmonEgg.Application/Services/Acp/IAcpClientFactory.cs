using SalmonEgg.Acp.Client;
using SalmonEgg.Domain.Interfaces.Transport;

namespace SalmonEgg.Application.Services.Acp;

/// <summary>
/// Host seam that turns a Domain transport into an ACP client.
/// Lives in Application because it bridges Domain transport identity with the ACP SDK.
/// </summary>
public interface IAcpClientFactory
{
    IAcpClient CreateClient(ITransport transport);
}
