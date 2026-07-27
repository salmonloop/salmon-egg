using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SalmonEgg.Acp.Protocol;

/// <summary>
/// ACP Slash Commands types.
/// https://agentclientprotocol.com/protocol/slash-commands
/// </summary>
public record AvailableCommandsUpdate : SessionUpdate
{
    [JsonPropertyName("availableCommands")]
    public List<AvailableCommand> AvailableCommands { get; set; } = new();
}

public record AvailableCommand : AcpProtocolObject
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("input")]
    public AvailableCommandInput? Input { get; set; }
}

public record AvailableCommandInput : AcpProtocolObject
{
    [JsonPropertyName("hint")]
    public string Hint { get; set; } = string.Empty;
}
