using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SalmonEgg.Infrastructure.Storage;

internal sealed class ConfigSyncPackageManifest
{
    public int SchemaVersion { get; set; } = 1;

    public string AppId { get; set; } = "SalmonEgg";

    public string CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow.ToString("O");

    public bool IncludesSecrets { get; set; }

    public List<string> Files { get; set; } = new();
}

public sealed class ConfigurationSecretSnapshot
{
    public int SchemaVersion { get; set; } = 1;

    public List<ConfigurationSecretEntry> Entries { get; set; } = new();
}

public sealed class ConfigurationSecretEntry
{
    public string ProfileId { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;
}

public sealed class CloudConfigSyncState
{
    public int SchemaVersion { get; set; } = 1;

    public string DeviceId { get; set; } = Guid.NewGuid().ToString("N");

    public string ProviderId { get; set; } = string.Empty;

    public string RemoteETag { get; set; } = string.Empty;

    public string LastSyncUtc { get; set; } = string.Empty;
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ConfigSyncPackageManifest))]
[JsonSerializable(typeof(ConfigurationSecretSnapshot))]
[JsonSerializable(typeof(ConfigurationSecretEntry))]
internal partial class ConfigSyncJsonContext : JsonSerializerContext
{
}
