using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace SalmonEgg.Infrastructure.Storage;

/// <summary>
/// 云配置内容寻址指纹的唯一 owner。
/// 规范化时剔除易变元数据（UpdatedAtUtc 等），使指纹只反映业务内容。
/// 不参与方向判定的时间语义；方向由 CloudSyncContentDecisionMaker 纯函数决定。
/// </summary>
public sealed class ConfigContentFingerprint
{
    private const string ConfigEntryPrefix = "files/config/";
    private const string SecretsEntryName = "secrets.json";
    private const string UpdatedAtUtcKey = "updated_at_utc";

    private readonly IAppDataService _appData;
    private readonly ConfigurationSecretSnapshotService _secrets;

    public ConfigContentFingerprint(
        IAppDataService appData,
        ConfigurationSecretSnapshotService secrets)
    {
        _appData = appData ?? throw new ArgumentNullException(nameof(appData));
        _secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
    }

    /// <summary>
    /// 对本地 config 目录（及可选 secrets）做规范化哈希。
    /// settingsOverride 非空时，app.yaml 使用 override 序列化结果而非磁盘内容。
    /// </summary>
    public async Task<string> ComputeLocalAsync(
        bool includeSecrets,
        AppSettings? settingsOverride = null,
        string? providerId = null,
        IReadOnlyDictionary<string, CloudSecretUpdate>? secretOverrides = null,
        CancellationToken cancellationToken = default)
    {
        var entries = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var appSettingsPath = Path.Combine(_appData.ConfigRootPath, "app.yaml");

        if (Directory.Exists(_appData.ConfigRootPath))
        {
            foreach (var path in Directory.EnumerateFiles(_appData.ConfigRootPath, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = ToZipRelativePath(path);
                if (settingsOverride is not null &&
                    string.Equals(path, appSettingsPath, StringComparison.Ordinal))
                {
                    entries[relative] = NormalizeConfigContent(
                        relative,
                        AppSettingsService.Serialize(settingsOverride));
                    continue;
                }

                var raw = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
                entries[relative] = NormalizeConfigContent(relative, raw);
            }
        }

        if (settingsOverride is not null && !entries.ContainsKey("app.yaml"))
        {
            entries["app.yaml"] = NormalizeConfigContent(
                "app.yaml",
                AppSettingsService.Serialize(settingsOverride));
        }

        if (includeSecrets)
        {
            var snapshot = await _secrets.ExportAsync(providerId, secretOverrides, cancellationToken)
                .ConfigureAwait(false);
            entries[SecretsEntryName] = NormalizeSecrets(snapshot);
        }

        return HashEntries(entries);
    }

    /// <summary>
    /// 对远端/本地包字节做规范化哈希（仅 files/config/ + 可选 secrets.json；剔除 manifest 时间戳）。
    /// </summary>
    public string ComputeFromPackage(byte[] package, bool includeSecrets)
    {
        if (package is null) throw new ArgumentNullException(nameof(package));

        var entries = new SortedDictionary<string, string>(StringComparer.Ordinal);
        using var stream = new MemoryStream(package, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.StartsWith(ConfigEntryPrefix, StringComparison.Ordinal))
            {
                var relative = entry.FullName.Substring(ConfigEntryPrefix.Length);
                if (string.IsNullOrWhiteSpace(relative) || relative.EndsWith("/", StringComparison.Ordinal))
                {
                    continue;
                }

                entries[relative] = NormalizeConfigContent(relative, ReadEntryText(entry));
            }
        }

        if (includeSecrets)
        {
            // 缺省 secrets.json 视为空快照，与本地 Export 空结果对齐，避免 includeSecrets 时误判 dirty。
            var snapshot = new ConfigurationSecretSnapshot();
            var secretsEntry = archive.GetEntry(SecretsEntryName);
            if (secretsEntry is not null)
            {
                snapshot = JsonSerializer.Deserialize(
                    ReadEntryText(secretsEntry),
                    ConfigSyncJsonContext.Default.ConfigurationSecretSnapshot) ?? snapshot;
            }

            entries[SecretsEntryName] = NormalizeSecrets(snapshot);
        }

        return HashEntries(entries);
    }

    private string ToZipRelativePath(string fullPath)
    {
        var relative = Path.GetRelativePath(_appData.ConfigRootPath, fullPath);
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
        {
            throw new InvalidDataException("Config file path escapes the config root.");
        }

        return relative.Replace(Path.DirectorySeparatorChar, '/');
    }

    private static string NormalizeConfigContent(string relativePath, string raw)
    {
        if (relativePath.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) ||
            relativePath.EndsWith(".yml", StringComparison.OrdinalIgnoreCase))
        {
            return NormalizeYamlStrippingUpdatedAt(raw);
        }

        return NormalizeNewlines(raw);
    }

    /// <summary>
    /// 反序列化 YAML → 剔除根级 updated_at_utc → 重新规范序列化。
    /// 无法解析时回退为去换行差异的原文，避免指纹服务拖垮同步。
    /// </summary>
    private static string NormalizeYamlStrippingUpdatedAt(string raw)
    {
        try
        {
            var yaml = new YamlStream();
            using (var reader = new StringReader(raw))
            {
                yaml.Load(reader);
            }

            if (yaml.Documents.Count == 0)
            {
                return string.Empty;
            }

            if (yaml.Documents[0].RootNode is YamlMappingNode mapping)
            {
                YamlNode? keyToRemove = null;
                foreach (var child in mapping.Children)
                {
                    if (child.Key is YamlScalarNode scalar &&
                        string.Equals(scalar.Value, UpdatedAtUtcKey, StringComparison.Ordinal))
                    {
                        keyToRemove = child.Key;
                        break;
                    }
                }

                if (keyToRemove is not null)
                {
                    mapping.Children.Remove(keyToRemove);
                }
            }

            // 通过中间对象再序列化，保证字段顺序与风格稳定（underscored）。
            var intermediate = ConvertNode(yaml.Documents[0].RootNode);
            var serializer = new SerializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .DisableAliases()
                .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
                .Build();
            return NormalizeNewlines(serializer.Serialize(intermediate));
        }
        catch (YamlException)
        {
            return NormalizeNewlines(raw);
        }
        catch (InvalidOperationException)
        {
            return NormalizeNewlines(raw);
        }
    }

    private static object? ConvertNode(YamlNode node) => node switch
    {
        YamlScalarNode scalar => ConvertScalar(scalar),
        YamlSequenceNode sequence => sequence.Children.Select(ConvertNode).ToList(),
        YamlMappingNode mapping => ConvertMapping(mapping),
        _ => null
    };

    private static object? ConvertScalar(YamlScalarNode scalar)
    {
        var value = scalar.Value;
        if (value is null)
        {
            return null;
        }

        // 保持字符串形态，避免 bool/number 重解析改变语义。
        return value;
    }

    private static Dictionary<string, object?> ConvertMapping(YamlMappingNode mapping)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var child in mapping.Children)
        {
            if (child.Key is not YamlScalarNode key || key.Value is null)
            {
                continue;
            }

            result[key.Value] = ConvertNode(child.Value);
        }

        return result;
    }

    private static string NormalizeSecrets(ConfigurationSecretSnapshot snapshot)
    {
        // 规范化：按 profileId/kind 排序后重序列化，避免导出顺序抖动。
        var normalized = new ConfigurationSecretSnapshot
        {
            SchemaVersion = snapshot.SchemaVersion,
            Entries = snapshot.Entries
                .OrderBy(e => e.ProfileId, StringComparer.Ordinal)
                .ThenBy(e => e.Kind, StringComparer.Ordinal)
                .Select(e => new ConfigurationSecretEntry
                {
                    ProfileId = e.ProfileId ?? string.Empty,
                    Kind = e.Kind ?? string.Empty,
                    Value = e.Value ?? string.Empty
                })
                .ToList()
        };

        return JsonSerializer.Serialize(normalized, ConfigSyncJsonContext.Default.ConfigurationSecretSnapshot);
    }

    private static string HashEntries(SortedDictionary<string, string> entries)
    {
        using var sha = SHA256.Create();
        foreach (var pair in entries)
        {
            var pathBytes = Encoding.UTF8.GetBytes(pair.Key);
            sha.TransformBlock(pathBytes, 0, pathBytes.Length, null, 0);
            sha.TransformBlock(new byte[] { 0 }, 0, 1, null, 0);
            var contentBytes = Encoding.UTF8.GetBytes(pair.Value);
            sha.TransformBlock(contentBytes, 0, contentBytes.Length, null, 0);
            sha.TransformBlock(new byte[] { 0 }, 0, 1, null, 0);
        }

        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(sha.Hash ?? Array.Empty<byte>()).ToLowerInvariant();
    }

    private static string ReadEntryText(ZipArchiveEntry entry)
    {
        using var input = entry.Open();
        using var reader = new StreamReader(input, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static string NormalizeNewlines(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
}
