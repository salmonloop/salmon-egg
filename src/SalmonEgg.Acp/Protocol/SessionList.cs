using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SalmonEgg.Acp.Protocol;

public sealed record SessionListParams : AcpProtocolObject
{
    [JsonPropertyName("cwd")]
    public string? Cwd { get; init; }

    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

}

public sealed record SessionListResponse : AcpProtocolObject
{
    [JsonPropertyName("sessions")]
    public List<AgentSessionInfo> Sessions { get; init; } = new();

    [JsonPropertyName("nextCursor")]
    public string? NextCursor { get; init; }

}

public sealed record AgentSessionInfo : AcpProtocolObject
{
    [JsonPropertyName("sessionId")]
    public string SessionId { get; init; } = string.Empty;

    [JsonPropertyName("cwd")]
    public string Cwd { get; init; } = string.Empty;

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("updatedAt")]
    public string? UpdatedAt { get; init; }

    [JsonPropertyName("additionalDirectories")]
    public List<string>? AdditionalDirectories { get; init; }

}
