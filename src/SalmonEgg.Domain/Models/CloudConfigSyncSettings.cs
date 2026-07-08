namespace SalmonEgg.Domain.Models;

using System.Collections.Generic;

public sealed class CloudConfigSyncSettings
{
    public bool Enabled { get; set; }

    public string ProviderId { get; set; } = string.Empty;

    public bool IncludeSecrets { get; set; } = true;

    public Dictionary<string, Dictionary<string, string>> ProviderOptions { get; set; } = new();
}
