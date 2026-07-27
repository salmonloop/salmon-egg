using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SalmonEgg.Acp.Protocol;

/// <summary>
/// ACP Slash Commands types.
/// https://agentclientprotocol.com/protocol/slash-commands
/// </summary>
public sealed record AvailableCommandsUpdate : SessionUpdate
{
    [JsonPropertyName("availableCommands")]
    public List<AvailableCommand> AvailableCommands { get; init; } = new();
}

public sealed record AvailableCommand : AcpProtocolObject
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("input")]
    public AvailableCommandInput? Input { get; init; }
}

public sealed record AvailableCommandInput : AcpProtocolObject
{
    [JsonPropertyName("hint")]
    public string Hint { get; init; } = string.Empty;
}
