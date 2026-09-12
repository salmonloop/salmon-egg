using SalmonEgg.Acp.Protocol;

namespace SalmonEgg.Application.Services.Acp;

/// <summary>Application-scoped opt-in policy. The default never offers or enables a draft protocol.</summary>
public sealed record AcpProtocolExperimentPolicy(bool EnableDraftV2 = false)
{
    public int OfferedProtocolVersion => EnableDraftV2 ? AcpProtocolVersion.V2 : AcpProtocolVersion.Default;
}
