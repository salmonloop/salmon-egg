using System.Text.Json.Serialization;

namespace SalmonEgg.Acp.Protocol;

/// <summary>
/// A permission option supplied by the Agent in `session/request_permission`.
/// </summary>
public sealed record PermissionOption : AcpProtocolObject
{
    /// <summary>
    /// Unique identifier for this option.
    /// </summary>
    [JsonPropertyName("optionId")]
    public string OptionId { get; init; } = string.Empty;

    /// <summary>
    /// Human-readable label displayed to the user.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// ACP permission option kind such as `allow_once` or `reject_always`.
    /// </summary>
    [JsonPropertyName("kind")]
    public string Kind { get; init; } = string.Empty;

    public PermissionOption()
    {
    }

    public PermissionOption(string optionId, string name, string kind)
    {
        OptionId = optionId;
        Name = name;
        Kind = kind;
    }
}
