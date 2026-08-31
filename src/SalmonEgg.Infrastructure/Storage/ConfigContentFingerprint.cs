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
/// 本地指纹必须经 <see cref="ConfigSyncPackageService.CreatePackageAsync"/> 投影后再哈希，
/// 与远端包共用同一规范化路径，禁止双实现漂移。
/// 方向判定不读时钟；由 <see cref="CloudSyncContentDecisionMaker"/> 纯函数决定。
/// </summary>
public sealed class ConfigContentFingerprint
{
    private const string ConfigEntryPrefix = "files/config/";
    private const string SecretsEntryName = "secrets.json";
    private const string UpdatedAtUtcKey = "updated_at_utc";

    private readonly ConfigSyncPackageService _packageService;

    public ConfigContentFingerprint(ConfigSyncPackageService packageService)
    {
        _packageService = packageService ?? throw new ArgumentNullException(nameof(packageService));
    }

    /// <summary>
    /// 对本地 config 做与打包完全相同的投影后哈希。
    /// settingsOverride 非空时，app.yaml 使用 override（与 CreatePackageAsync 一致）。
    /// </summary>
    public async Task<string> ComputeLocalAsync(
        bool includeSecrets,
        AppSettings? settingsOverride = null,
        string? providerId = null,
        IReadOnlyDictionary<string, CloudSecretUpdate>? secretOverrides = null,
        CancellationToken cancellationToken = default)
    {
        var package = await _packageService.CreatePackageAsync(
                includeSecrets,
                settingsOverride,
                providerId,
                secretOverrides,
                cancellationToken)
            .ConfigureAwait(false);
        return ComputeFromPackage(package, includeSecrets);
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
            if (!entry.FullName.StartsWith(ConfigEntryPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            var relative = entry.FullName.Substring(ConfigEntryPrefix.Length);
            if (string.IsNullOrWhiteSpace(relative) || relative.EndsWith("/", StringComparison.Ordinal))
            {
                continue;
            }

            entries[relative] = NormalizeConfigContent(relative, ReadEntryText(entry));
        }

        if (includeSecrets)
        {
            // 缺省 secrets.json 视为空快照，与本地 Export 空结果对齐。
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
    /// 反序列化 YAML → 剔除根级 updated_at_utc → key 排序后规范序列化。
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
        YamlScalarNode scalar => scalar.Value,
        YamlSequenceNode sequence => sequence.Children.Select(ConvertNode).ToList(),
        YamlMappingNode mapping => ConvertMapping(mapping),
        _ => null
    };

    private static SortedDictionary<string, object?> ConvertMapping(YamlMappingNode mapping)
    {
        // key 按 ordinal 排序，消除写出顺序导致的指纹抖动。
        var result = new SortedDictionary<string, object?>(StringComparer.Ordinal);
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
