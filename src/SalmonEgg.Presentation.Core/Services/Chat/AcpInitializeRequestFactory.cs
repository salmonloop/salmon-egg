using SalmonEgg.Acp.Protocol;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Presentation.Core.Services.Chat;

internal static class AcpInitializeRequestFactory
{
    public static InitializeParams CreateDefault(IPlatformCapabilityService? platformCapabilities = null,
        int protocolVersion = AcpProtocolVersion.Default)
        => new()
        {
            ProtocolVersion = protocolVersion,
            ClientInfo = new ClientInfo
            {
                Name = "SalmonEgg",
                Title = "SalmonEgg",
                Version = "1.0.0"
            },
            ClientCapabilities = CreateCapabilities(platformCapabilities, protocolVersion)
        };

    private static ClientCapabilities CreateCapabilities(IPlatformCapabilityService? platformCapabilities, int protocolVersion)
    {
        var defaults = ClientCapabilityDefaults.Create();
        return defaults with
        {
            Session = protocolVersion == AcpProtocolVersion.V2 ? null : defaults.Session,
            Fs = protocolVersion == AcpProtocolVersion.V2 ? null : defaults.Fs,
            Terminal = protocolVersion == AcpProtocolVersion.V2 ? null : defaults.Terminal,
            Elicitation = defaults.Elicitation! with
            {
                Url = platformCapabilities?.SupportsUrlElicitation == true ? new ElicitationUrlCapabilities() : null
            }
        };
    }
}
