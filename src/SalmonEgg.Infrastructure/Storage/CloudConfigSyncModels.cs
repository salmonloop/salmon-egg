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

    // 乐观并发令牌，仅用于上传时的 If-Match 防 clobber；不再参与同步方向判定。
    public string RemoteETag { get; set; } = string.Empty;

    // 上次同步成功时落地内容的规范化指纹（内容寻址 3-way 判定的基线）。
    // 缺省空串表示「基线未建立」，老状态读入即视为首次采用。
    public string SyncedFingerprint { get; set; } = string.Empty;

    // 写入 SyncedFingerprint 时使用的 IncludeSecrets 策略。
    // 与当前设置不一致时，旧指纹不可比，按基线未知处理（避免 secrets 策略翻转误判）。
    public bool SyncedIncludeSecrets { get; set; } = true;

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
