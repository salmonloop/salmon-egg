using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
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
    private readonly ILogger<ConfigSyncPackageService> _logger;

    public ConfigSyncPackageService(
        IAppDataService appData,
        ConfigurationSecretSnapshotService secrets,
        IConfigChangeSignal configChangeSignal,
        IFileSystemPersistence persistence,
        ILogger<ConfigSyncPackageService> logger)
    {
        _appData = appData ?? throw new ArgumentNullException(nameof(appData));
        _secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
        _configChangeSignal = configChangeSignal ?? throw new ArgumentNullException(nameof(configChangeSignal));
        _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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

        try
        {
            // Windows 上被占用/只读的文件常让递归删除中途失败并留下半删目录,
            // 因此删除与换入必须同处一个回滚保护窗,而不能只保护 Move 的后半窗。
            if (Directory.Exists(configRoot))
            {
                Directory.Delete(configRoot, recursive: true);
            }

            Directory.Move(stagingPath, configRoot);
        }
        catch (Exception swapFailure)
        {
            _logger.LogWarning(
                swapFailure,
                "Config root swap failed during restore; rolling back from backup. ConfigRoot: {ConfigRoot}, Backup: {BackupPath}",
                configRoot,
                backupPath);
            RollBackConfigRoot(configRoot, backupPath, swapFailure);
        }
    }

    [DoesNotReturn]
    private void RollBackConfigRoot(string configRoot, string backupPath, Exception swapFailure)
    {
        try
        {
            // 半删残留里往往正是导致换入失败的只读文件;先清属性删残留、再从 backup 整拷,
            // 保证回滚后 config root 与 backup 完全一致,而不是新旧混合状态。
            DeleteDirectoryClearingReadOnly(configRoot);
            if (Directory.Exists(backupPath))
            {
                CopyDirectory(backupPath, configRoot);
            }
        }
        catch (Exception rollbackFailure)
        {
            _logger.LogError(
                rollbackFailure,
                "Config root rollback failed after a failed swap; restore manually from backup. ConfigRoot: {ConfigRoot}, Backup: {BackupPath}",
                configRoot,
                backupPath);

            // 回滚失败不得吞掉/顶替原始换入异常:两者都进 AggregateException,
            // 消息携带 backup 路径供用户手动恢复。
            throw new AggregateException(
                $"Config restore failed and rolling back the config root also failed. Restore it manually from the backup at '{backupPath}'.",
                swapFailure,
                rollbackFailure);
        }

        _logger.LogWarning(
            "Config root rolled back from backup after a failed swap; local config is preserved. ConfigRoot: {ConfigRoot}, Backup: {BackupPath}",
            configRoot,
            backupPath);

        // 回滚成功:本地配置未损,原样重抛原始异常(保留类型与堆栈)表达"恢复操作失败"。
        ExceptionDispatchInfo.Capture(swapFailure).Throw();
    }

    private static void DeleteDirectoryClearingReadOnly(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        foreach (var directory in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(directory, FileAttributes.Directory);
        }

        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(path, recursive: true);
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
