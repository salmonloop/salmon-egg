using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SalmonEgg.Acp.Protocol;

/// <summary>
/// ACP Slash Commands types.
/// https://agentclientprotocol.com/protocol/slash-commands
/// </summary>
public class AvailableCommandsUpdate : SessionUpdate
{
    [JsonPropertyName("availableCommands")]
    public List<AvailableCommand> AvailableCommands { get; set; } = new();
}

public class AvailableCommand : AcpProtocolObject
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("input")]
    public AvailableCommandInput? Input { get; set; }
}

public class AvailableCommandInput : AcpProtocolObject
{
    [JsonPropertyName("hint")]
    public string Hint { get; set; } = string.Empty;
}
