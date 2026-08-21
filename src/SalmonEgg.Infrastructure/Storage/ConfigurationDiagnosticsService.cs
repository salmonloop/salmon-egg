using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using SalmonEgg.Infrastructure.Storage.YamlModels;
using YamlDotNet.Core;

namespace SalmonEgg.Infrastructure.Storage;

/// <summary>
/// 只读配置诊断：报告 app.yaml、各 server YAML 与配置包内文件的 schema 版本与可解析性。
/// </summary>
/// <remarks>
/// 与 <see cref="AppSettingsService"/> / <see cref="ConfigurationManager"/> 共享同一套
/// YAML 模型与宽容读语义，是它们的「体检」视图而非第二套读取 owner：本类型从不写入，
/// 也不做迁移；「高版本拒绝写回」的执行仍由两个服务各自的写入守卫负责，这里只是把
/// 同样的判据提前暴露给用户（写盘前）与导入方（换入 config root 前）。
///
/// 诊断消息只含文件名、schema 版本与失败类别，绝不含文件内容——凭据在 ISecureStorage
/// 里，但文件名、路径片段等也可能被当作敏感信息对待，因此统一只报相对路径。
/// </remarks>
public sealed class ConfigurationDiagnosticsService
{
    private const string PackageConfigEntryPrefix = "files/config/";
    private const string PackageManifestEntryName = "manifest.json";

    private readonly IAppDataService _appData;
    private readonly IAppFileStore _fileStore;

    public ConfigurationDiagnosticsService(IAppDataService appData, IAppFileStore fileStore)
    {
        _appData = appData ?? throw new ArgumentNullException(nameof(appData));
        _fileStore = fileStore ?? throw new ArgumentNullException(nameof(fileStore));
    }

    /// <summary>app.yaml 支持的 schema 版本。</summary>
    public int SupportedAppSettingsSchemaVersion => AppSettingsService.CurrentAppSettingsSchemaVersion;

    /// <summary>server 配置支持的 schema 版本。</summary>
    public int SupportedServerConfigurationSchemaVersion => ConfigurationManager.CurrentServerConfigurationSchemaVersion;

    /// <summary>
    /// 检查磁盘上的全部配置文件，返回逐文件诊断。
    /// </summary>
    public async Task<IReadOnlyList<ConfigurationDiagnostic>> InspectAsync(
        CancellationToken cancellationToken = default)
    {
        var results = new List<ConfigurationDiagnostic>();
        results.Add(await InspectAppSettingsAsync(cancellationToken).ConfigureAwait(false));
        results.AddRange(await InspectServerConfigurationsAsync(cancellationToken).ConfigureAwait(false));
        return results;
    }

    /// <summary>
    /// 检查一个配置包的字节内容，返回逐条目诊断；不做任何落盘或换入。
    /// </summary>
    /// <remarks>
    /// 缺 manifest 的包以单条 <see cref="ConfigurationDiagnosticKind.Unparsable"/> 报告，
    /// 让调用方在 <see cref="ConfigSyncPackageService.RestorePackageAsync"/> 抛
    /// <see cref="InvalidDataException"/> 之前就能给出可操作的错误信息。
    /// </remarks>
    public IReadOnlyList<ConfigurationDiagnostic> InspectPackage(byte[] package)
    {
        if (package is null) throw new ArgumentNullException(nameof(package));

        using var stream = new MemoryStream(package, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        if (archive.GetEntry(PackageManifestEntryName) is null)
        {
            return [new ConfigurationDiagnostic(
                PackageManifestEntryName,
                ConfigurationDiagnosticKind.Unparsable,
                null,
                "package is missing manifest.json and is not identifiable as a Salmon Egg configuration package")];
        }

        var results = new List<ConfigurationDiagnostic>();
        foreach (var entry in archive.Entries
                     .Where(e => e.FullName.StartsWith(PackageConfigEntryPrefix, StringComparison.Ordinal))
                     .OrderBy(e => e.FullName, StringComparer.Ordinal))
        {
            var relativeName = entry.FullName.Substring(PackageConfigEntryPrefix.Length);
            if (string.IsNullOrWhiteSpace(relativeName) || relativeName.EndsWith("/", StringComparison.Ordinal))
            {
                continue;
            }

            results.Add(InspectPackageEntry(entry, relativeName));
        }

        return results;
    }

    private ConfigurationDiagnostic InspectPackageEntry(ZipArchiveEntry entry, string relativeName)
    {
        try
        {
            using var input = entry.Open();
            using var reader = new StreamReader(input);
            var yaml = reader.ReadToEndAsync().GetAwaiter().GetResult();

            // app.yaml 与 servers/*.yaml 各有专属模型与支持版本，按包内路径分流，
            // 使「拒绝导入」的阈值与对应服务写入守卫的阈值完全一致。
            if (string.Equals(relativeName, "app.yaml", StringComparison.Ordinal))
            {
                var model = YamlSerialization.CreateDeserializer().Deserialize<AppSettingsYamlV1>(yaml);
                return DescribeSchema(relativeName, model.SchemaVersion, AppSettingsService.CurrentAppSettingsSchemaVersion);
            }

            var serverModel = YamlSerialization.CreateDeserializer().Deserialize<ServerConfigurationYaml>(yaml);
            return DescribeSchema(relativeName, serverModel.SchemaVersion, ConfigurationManager.CurrentServerConfigurationSchemaVersion);
        }
        catch (YamlException exception)
        {
            return ConfigurationDiagnostic.Unparsable(relativeName, exception.Message);
        }
        catch (IOException exception)
        {
            return ConfigurationDiagnostic.Unparsable(relativeName, exception.GetType().Name);
        }
    }

    private async Task<ConfigurationDiagnostic> InspectAppSettingsAsync(CancellationToken cancellationToken)
    {
        var path = Path.Combine(_appData.ConfigRootPath, "app.yaml");
        var yaml = await ReadOrNullAsync(path, cancellationToken).ConfigureAwait(false);
        if (yaml is null)
        {
            return ConfigurationDiagnostic.Absent("app.yaml");
        }

        try
        {
            var model = YamlSerialization.CreateDeserializer().Deserialize<AppSettingsYamlV1>(yaml);
            return DescribeSchema("app.yaml", model.SchemaVersion, AppSettingsService.CurrentAppSettingsSchemaVersion);
        }
        catch (YamlException exception)
        {
            return ConfigurationDiagnostic.Unparsable("app.yaml", exception.Message);
        }
    }

    private async Task<IEnumerable<ConfigurationDiagnostic>> InspectServerConfigurationsAsync(
        CancellationToken cancellationToken)
    {
        var serversDirectory = Path.Combine(_appData.ConfigRootPath, "servers");
        if (!Directory.Exists(serversDirectory))
        {
            return [];
        }

        var results = new List<ConfigurationDiagnostic>();
        foreach (var path in Directory.EnumerateFiles(serversDirectory, "*.yaml").OrderBy(p => p, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativeName = Path.GetRelativePath(_appData.ConfigRootPath, path);
            var yaml = await ReadOrNullAsync(path, cancellationToken).ConfigureAwait(false);
            if (yaml is null)
            {
                continue;
            }

            try
            {
                var model = YamlSerialization.CreateDeserializer().Deserialize<ServerConfigurationYaml>(yaml);
                results.Add(DescribeSchema(relativeName, model.SchemaVersion, ConfigurationManager.CurrentServerConfigurationSchemaVersion));
            }
            catch (YamlException exception)
            {
                results.Add(ConfigurationDiagnostic.Unparsable(relativeName, exception.Message));
            }
        }

        return results;
    }

    private async Task<string?> ReadOrNullAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            return await _fileStore.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 读不了也是一种诊断结果，但不能让整个检查中断。
            return null;
        }
    }

    private static ConfigurationDiagnostic DescribeSchema(string name, int schemaVersion, int supportedVersion) =>
        schemaVersion > supportedVersion
            ? ConfigurationDiagnostic.TooNew(name, schemaVersion, supportedVersion)
            : ConfigurationDiagnostic.Ok(name, schemaVersion);
}

/// <summary>
/// 单个配置文件的诊断结论。
/// </summary>
public sealed record ConfigurationDiagnostic(
    string FileName,
    ConfigurationDiagnosticKind Kind,
    int? SchemaVersion,
    string? Detail)
{
    internal static ConfigurationDiagnostic Ok(string fileName, int schemaVersion) =>
        new(fileName, ConfigurationDiagnosticKind.Ok, schemaVersion, null);

    internal static ConfigurationDiagnostic Absent(string fileName) =>
        new(fileName, ConfigurationDiagnosticKind.Absent, null, null);

    internal static ConfigurationDiagnostic TooNew(string fileName, int schemaVersion, int supportedVersion) =>
        new(
            fileName,
            ConfigurationDiagnosticKind.SchemaTooNew,
            schemaVersion,
            $"schema_version {schemaVersion} is newer than supported version {supportedVersion}. "
            + "Writes to this file are refused; upgrade Salmon Egg to migrate it.");

    internal static ConfigurationDiagnostic Unparsable(string fileName, string detail) =>
        new(fileName, ConfigurationDiagnosticKind.Unparsable, null, detail);
}

public enum ConfigurationDiagnosticKind
{
    Ok,
    Absent,
    SchemaTooNew,
    Unparsable
}
