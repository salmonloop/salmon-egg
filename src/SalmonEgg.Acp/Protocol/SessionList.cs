using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SalmonEgg.Acp.Protocol;

public class SessionListParams
{
    [JsonPropertyName("cwd")]
    public string? Cwd { get; set; }

    [JsonPropertyName("cursor")]
    public string? Cursor { get; set; }

    [JsonPropertyName("_meta")]
    public Dictionary<string, object?>? Meta { get; set; }

    [JsonExtensionData]
    public Dictionary<string, object?>? ExtraParams { get; set; }
}

public class SessionListResponse
{
    [JsonPropertyName("sessions")]
    public List<AgentSessionInfo> Sessions { get; set; } = new();

    [JsonPropertyName("nextCursor")]
    public string? NextCursor { get; set; }

    [JsonPropertyName("_meta")]
    public Dictionary<string, object?>? Meta { get; set; }

    [JsonExtensionData]
    public Dictionary<string, object?>? ExtraData { get; set; }
}

public class AgentSessionInfo
{
    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("cwd")]
    public string Cwd { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("updatedAt")]
    public string? UpdatedAt { get; set; }

    [JsonPropertyName("additionalDirectories")]
    public List<string>? AdditionalDirectories { get; set; }

    [JsonPropertyName("_meta")]
    public Dictionary<string, object?>? Meta { get; set; }
}
