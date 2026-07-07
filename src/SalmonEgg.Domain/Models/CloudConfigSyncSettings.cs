namespace SalmonEgg.Domain.Models;

public sealed class CloudConfigSyncSettings
{
    public bool Enabled { get; set; }

    public string ProviderId { get; set; } = string.Empty;

    public bool IncludeSecrets { get; set; } = true;
}
