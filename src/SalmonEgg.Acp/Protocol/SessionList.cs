using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SalmonEgg.Acp.Protocol;

public record SessionListParams : AcpProtocolObject
{
    [JsonPropertyName("cwd")]
    public string? Cwd { get; set; }

    [JsonPropertyName("cursor")]
    public string? Cursor { get; set; }

}

public record SessionListResponse : AcpProtocolObject
{
    [JsonPropertyName("sessions")]
    public List<AgentSessionInfo> Sessions { get; set; } = new();

    [JsonPropertyName("nextCursor")]
    public string? NextCursor { get; set; }

}

public record AgentSessionInfo : AcpProtocolObject
{
    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("cwd")]
    public string Cwd { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("updatedAt")]
    public string? UpdatedAt { get; set; }

    [JsonPropertyName("additionalDirectories")]
    public List<string>? AdditionalDirectories { get; set; }

}
