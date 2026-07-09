using SalmonEgg.Acp.Protocol;

namespace SalmonEgg.Presentation.Core.Services.Chat;

internal static class AcpInitializeRequestFactory
{
    public static InitializeParams CreateDefault()
        => new()
        {
            ProtocolVersion = 1,
            ClientInfo = new ClientInfo
            {
                Name = "SalmonEgg",
                Title = "SalmonEgg",
                Version = "1.0.0"
            },
            ClientCapabilities = ClientCapabilityDefaults.Create()
        };
}
