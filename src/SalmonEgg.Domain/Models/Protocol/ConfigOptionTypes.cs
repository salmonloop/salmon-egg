using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SalmonEgg.Domain.Models.Protocol;

/// <summary>
/// ACP Session Config Options types.
/// https://agentclientprotocol.com/protocol/session-config-options
/// </summary>
public class ConfigOption
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("category")]
    public string? Category { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = "select";

    [JsonPropertyName("currentValue")]
    public string CurrentValue { get; set; } = string.Empty;

    [JsonPropertyName("options")]
    public List<ConfigOptionValue> Options { get; set; } = new();
}

public class ConfigOptionValue
{
    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

