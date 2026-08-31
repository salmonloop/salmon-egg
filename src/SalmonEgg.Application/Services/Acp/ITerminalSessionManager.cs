using SalmonEgg.Acp.Client;

namespace SalmonEgg.Application.Services.Acp;

/// <summary>
/// Host-owned ACP terminal session manager.
/// Extends the SDK terminal seam so Infrastructure can implement platform terminal ownership
/// without Domain depending on ACP client types.
/// </summary>
public interface ITerminalSessionManager : IAcpTerminalSessionManager
{
}
