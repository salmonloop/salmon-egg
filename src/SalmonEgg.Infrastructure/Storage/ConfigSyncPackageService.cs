using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Infrastructure.Storage;

public sealed class ConfigSyncPackageService
{
    private const int BufferSize = 81920;
    private const string ManifestEntryName = "manifest.json";
    private const string SecretsEntryName = "secrets.json";
    private const string ConfigEntryPrefix = "files/config/";

    private readonly IAppDataService _appData;
    private readonly ConfigurationSecretSnapshotService _secrets;
    private readonly IConfigChangeSignal _configChangeSignal;
    private readonly IFileSystemPersistence _persistence;

    public ConfigSyncPackageService(
        IAppDataService appData,
        ConfigurationSecretSnapshotService secrets,
        IConfigChangeSignal configChangeSignal,
        IFileSystemPersistence persistence)
    {
        _appData = appData ?? throw new ArgumentNullException(nameof(appData));
        _secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
        _configChangeSignal = configChangeSignal ?? throw new ArgumentNullException(nameof(configChangeSignal));
        _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
    }

    public async Task<byte[]> CreatePackageAsync(bool includeSecrets, CancellationToken cancellationToken = default)
        => await CreatePackageAsync(includeSecrets, null, null, null, cancellationToken).ConfigureAwait(false);

    public async Task<byte[]> CreatePackageAsync(
        bool includeSecrets,
        AppSettings? settingsOverride,
        string? providerId,
        IReadOnlyDictionary<string, CloudSecretUpdate>? secretOverrides,
        CancellationToken cancellationToken = default)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var files = GetConfigFiles().ToList();
            var appSettingsPath = Path.Combine(_appData.ConfigRootPath, "app.yaml");
            if (settingsOverride is not null && !files.Contains(appSettingsPath, StringComparer.Ordinal))
            {
                files.Add(appSettingsPath);
            }

            var manifest = new ConfigSyncPackageManifest
            {
                CreatedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
                IncludesSecrets = includeSecrets,
                Files = files.Select(GetRelativeConfigPath).OrderBy(x => x, StringComparer.Ordinal).ToList()
            };

            await WriteManifestEntryAsync(archive, manifest, cancellationToken).ConfigureAwait(false);

            foreach (var path in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (settingsOverride is not null && string.Equals(path, appSettingsPath, StringComparison.Ordinal))
                {
                    await WriteBytesEntryAsync(
                            archive,
                            ConfigEntryPrefix + "app.yaml",
                            Encoding.UTF8.GetBytes(AppSettingsService.Serialize(settingsOverride)),
                            cancellationToken)
                        .ConfigureAwait(false);
                    continue;
                }

                await WriteFileEntryAsync(archive, ConfigEntryPrefix + ToZipPath(GetRelativeConfigPath(path)), path, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (includeSecrets)
            {
                var snapshot = await _secrets.ExportAsync(providerId, secretOverrides, cancellationToken).ConfigureAwait(false);
                await WriteSecretsEntryAsync(archive, snapshot, cancellationToken).ConfigureAwait(false);
            }
        }

        return stream.ToArray();
    }

    public async Task<string> RestorePackageAsync(byte[] package, CancellationToken cancellationToken = default)
    {
        if (package is null) throw new ArgumentNullException(nameof(package));

        await _persistence.LoadAsync(cancellationToken).ConfigureAwait(false);
        var backupPath = BackupCurrentConfig();
        using (var suppression = _configChangeSignal.Suppress())
        {
            using var stream = new MemoryStream(package, writable: false);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

            ValidateArchive(archive);

            // 先在同卷 staging 目录完整展开,成功后再整体换入:
            // 解包中途取消或 IO 失败不得让 config root 停在半删/半写状态。
            var stagingPath = _appData.ConfigRootPath + ".restore-" + Guid.NewGuid().ToString("N");
            try
            {
                foreach (var entry in archive.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!entry.FullName.StartsWith(ConfigEntryPrefix, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var relative = entry.FullName.Substring(ConfigEntryPrefix.Length);
                    if (string.IsNullOrWhiteSpace(relative) || relative.EndsWith("/", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var destination = ResolveEntryPath(stagingPath, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    using var input = entry.Open();
                    using var output = File.Create(destination);
                    await input.CopyToAsync(output, BufferSize, cancellationToken).ConfigureAwait(false);
                }

                SwapConfigRoot(stagingPath, backupPath);
            }
            finally
            {
                TryDeleteDirectory(stagingPath);
            }

            var secretsEntry = archive.GetEntry(SecretsEntryName);
            if (secretsEntry is not null)
            {
                using var input = secretsEntry.Open();
                var snapshot = await JsonSerializer.DeserializeAsync(
                        input,
                        ConfigSyncJsonContext.Default.ConfigurationSecretSnapshot,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (snapshot is not null)
                {
                    await _secrets.ImportAsync(snapshot, cancellationToken).ConfigureAwait(false);
                }
            }

            await _persistence.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        _configChangeSignal.NotifyChanged(_appData.ConfigRootPath, ConfigChangeKind.Restored);
        return backupPath;
    }

    /// <summary>
    /// 冲突 fail-closed 工件：本地 config 快照 + 远端包字节。
    /// 不修改当前 config，不发 Restored 信号；仅落盘供人工/后续 UI 决策。
    /// </summary>
    public async Task<string> PersistConflictArtifactsAsync(
        byte[] remotePackage,
        CancellationToken cancellationToken = default)
    {
        if (remotePackage is null) throw new ArgumentNullException(nameof(remotePackage));

        var artifactRoot = Path.Combine(_appData.AppDataRootPath, "config-conflict-artifacts");
        var artifactPath = Path.Combine(artifactRoot, DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff"));
        Directory.CreateDirectory(artifactPath);

        if (Directory.Exists(_appData.ConfigRootPath))
        {
            CopyDirectory(_appData.ConfigRootPath, Path.Combine(artifactPath, "local"));
        }

        var remotePackagePath = Path.Combine(artifactPath, "remote.package.zip");
        await File.WriteAllBytesAsync(remotePackagePath, remotePackage, cancellationToken).ConfigureAwait(false);
        return artifactPath;
    }

    private IEnumerable<string> GetConfigFiles()
    {
        if (!Directory.Exists(_appData.ConfigRootPath))
        {
            yield break;
        }

        foreach (var path in Directory.EnumerateFiles(_appData.ConfigRootPath, "*", SearchOption.AllDirectories))
        {
            yield return path;
        }
    }

    private string BackupCurrentConfig()
    {
        var backupRoot = Path.Combine(_appData.AppDataRootPath, "config-backups");
        var backupPath = Path.Combine(backupRoot, DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff"));
        if (!Directory.Exists(_appData.ConfigRootPath))
        {
            return backupPath;
        }

        CopyDirectory(_appData.ConfigRootPath, backupPath);
        return backupPath;
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, directory);
            Directory.CreateDirectory(Path.Combine(destination, relative));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static async Task WriteManifestEntryAsync(
        ZipArchive archive,
        ConfigSyncPackageManifest value,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(ManifestEntryName, CompressionLevel.Optimal);
        using var output = entry.Open();
        await JsonSerializer.SerializeAsync(
                output,
                value,
                ConfigSyncJsonContext.Default.ConfigSyncPackageManifest,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task WriteSecretsEntryAsync(
        ZipArchive archive,
        ConfigurationSecretSnapshot value,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(SecretsEntryName, CompressionLevel.Optimal);
        using var output = entry.Open();
        await JsonSerializer.SerializeAsync(
                output,
                value,
                ConfigSyncJsonContext.Default.ConfigurationSecretSnapshot,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task WriteFileEntryAsync(
        ZipArchive archive,
        string entryName,
        string path,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var input = File.OpenRead(path);
        using var output = entry.Open();
        await input.CopyToAsync(output, BufferSize, cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteBytesEntryAsync(
        ZipArchive archive,
        string entryName,
        byte[] content,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var output = entry.Open();
        await output.WriteAsync(content, cancellationToken).ConfigureAwait(false);
    }

    private void ValidateArchive(ZipArchive archive)
    {
        if (archive.GetEntry(ManifestEntryName) is null)
        {
            throw new InvalidDataException("Cloud config package is missing manifest.json.");
        }

        foreach (var entry in archive.Entries)
        {
            if (!entry.FullName.StartsWith(ConfigEntryPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            var relative = entry.FullName.Substring(ConfigEntryPrefix.Length);
            _ = ResolveConfigPath(relative);
        }
    }

    private string ResolveConfigPath(string zipRelativePath)
        => ResolveEntryPath(_appData.ConfigRootPath, zipRelativePath);

    private static string ResolveEntryPath(string rootPath, string zipRelativePath)
    {
        if (string.IsNullOrWhiteSpace(zipRelativePath))
        {
            throw new InvalidDataException("Cloud config package contains an empty path.");
        }

        var normalized = zipRelativePath.Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(normalized))
        {
            throw new InvalidDataException("Cloud config package contains an absolute path.");
        }

        var fullPath = Path.GetFullPath(Path.Combine(rootPath, normalized));
        var root = Path.GetFullPath(rootPath);
        if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
            !string.Equals(fullPath, root, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Cloud config package path escapes the config root.");
        }

        return fullPath;
    }

    private void SwapConfigRoot(string stagingPath, string backupPath)
    {
        var configRoot = _appData.ConfigRootPath;
        if (!Directory.Exists(stagingPath))
        {
            // 包内可以不含任何 config 条目,换入等价的空目录以保持原先"删除后重建"的语义。
            Directory.CreateDirectory(stagingPath);
        }

        if (Directory.Exists(configRoot))
        {
            Directory.Delete(configRoot, recursive: true);
        }

        try
        {
            Directory.Move(stagingPath, configRoot);
        }
        catch
        {
            // 删除与换入之间的窄窗失败:从本次 backup 拷回,避免 config root 消失。
            if (Directory.Exists(backupPath))
            {
                CopyDirectory(backupPath, configRoot);
            }

            throw;
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // 残留 staging 目录只是垃圾,不影响正确性,不因清理失败掩盖真实异常。
        }
    }

    private string GetRelativeConfigPath(string path)
    {
        var relative = Path.GetRelativePath(_appData.ConfigRootPath, path);
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
        {
            throw new InvalidDataException("Config file path escapes the config root.");
        }

        return relative;
    }

    private static string ToZipPath(string relativePath) => relativePath.Replace(Path.DirectorySeparatorChar, '/');
}
