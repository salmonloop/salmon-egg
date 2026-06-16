namespace SalmonEgg.Domain.Models;

public sealed class AgentRemoteDirectory
{
    public string ProfileId { get; set; } = string.Empty;
    public string DirectoryId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string RemotePath { get; set; } = string.Empty;
}
