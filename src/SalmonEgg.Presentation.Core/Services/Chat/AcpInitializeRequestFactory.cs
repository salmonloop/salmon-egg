using SalmonEgg.Acp.Protocol;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Presentation.Core.Services.Chat;

internal static class AcpInitializeRequestFactory
{
    public static InitializeParams CreateDefault(IPlatformCapabilityService? platformCapabilities = null)
        => new()
        {
            ProtocolVersion = AcpProtocolVersion.Default,
            ClientInfo = new ClientInfo
            {
                Name = "SalmonEgg",
                Title = "SalmonEgg",
                Version = "1.0.0"
            },
            ClientCapabilities = CreateCapabilities(platformCapabilities)
        };

    private static ClientCapabilities CreateCapabilities(IPlatformCapabilityService? platformCapabilities)
    {
        var defaults = ClientCapabilityDefaults.Create();
        return defaults with
        {
            Elicitation = defaults.Elicitation! with
            {
                Url = platformCapabilities?.SupportsUrlElicitation == true ? new ElicitationUrlCapabilities() : null
            }
        };
    }
}
