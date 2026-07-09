namespace SalmonEgg.Acp.Protocol;

/// <summary>
/// A permission option supplied by the Agent in `session/request_permission`.
/// </summary>
public sealed class PermissionOption
{
    /// <summary>
    /// Unique identifier for this option.
    /// </summary>
    public string OptionId { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable label displayed to the user.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// ACP permission option kind such as `allow_once` or `reject_always`.
    /// </summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>
    /// Optional description for the option.
    /// </summary>
    public string? Description { get; set; }

    public PermissionOption()
    {
    }

    public PermissionOption(string optionId, string name, string kind, string? description = null)
    {
        OptionId = optionId;
        Name = name;
        Kind = kind;
        Description = description;
    }
}
