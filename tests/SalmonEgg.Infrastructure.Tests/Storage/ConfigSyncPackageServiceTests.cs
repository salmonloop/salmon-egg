using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using SalmonEgg.Infrastructure.Storage;

namespace SalmonEgg.Infrastructure.Tests.Storage;

public sealed class ConfigSyncPackageServiceTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly AppDataService _appData;
    private readonly ConfigSyncPackageService _packageService;

    public ConfigSyncPackageServiceTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "SalmonEggConfigSyncPackageTests", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("SALMONEGG_APPDATA_ROOT", Path.Combine(_testDirectory, "SalmonEgg"), EnvironmentVariableTarget.Process);
        _appData = new AppDataService();
        var configChangeSignal = new ConfigChangeSignal();
        var fileStore = new FileSystemAppFileStore(new NoOpFileSystemPersistence(), configChangeSignal);
        var secrets = new ConfigurationSecretSnapshotService(
            new PlainTextFileSecureStorage(fileStore, _appData),
            fileStore,
            _appData);
        _packageService = new ConfigSyncPackageService(
            _appData,
            secrets,
            configChangeSignal,
            new NoOpFileSystemPersistence(),
            NullLogger<ConfigSyncPackageService>.Instance);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("SALMONEGG_APPDATA_ROOT", null, EnvironmentVariableTarget.Process);
        if (!Directory.Exists(_testDirectory))
        {
            return;
        }

        // 回滚测试会经 backup 恢复出只读文件;先清属性再删,避免清理自身被只读位阻断。
        foreach (var file in Directory.EnumerateFiles(_testDirectory, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(_testDirectory, recursive: true);
    }

    [Fact]
    public async Task RestorePackageAsync_ReplacesConfigRootWithPackageContentAndKeepsBackup()
    {
        Directory.CreateDirectory(_appData.ConfigRootPath);
        var appYamlPath = Path.Combine(_appData.ConfigRootPath, "app.yaml");
        var localOnlyPath = Path.Combine(_appData.ConfigRootPath, "local-only.yaml");
        await File.WriteAllTextAsync(appYamlPath, "schema_version: 2\ntheme: Light\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(localOnlyPath, "value: local", TestContext.Current.CancellationToken);

        var backupPath = await _packageService.RestorePackageAsync(
            CreateRemotePackage("theme: Dark"),
            TestContext.Current.CancellationToken);

        Assert.Contains(
            "theme: Dark",
            await File.ReadAllTextAsync(appYamlPath, TestContext.Current.CancellationToken),
            StringComparison.Ordinal);
        Assert.False(File.Exists(localOnlyPath));
        Assert.True(Directory.Exists(backupPath));
        Assert.Equal(
            "value: local",
            await File.ReadAllTextAsync(Path.Combine(backupPath, "local-only.yaml"), TestContext.Current.CancellationToken));
        Assert.Contains(
            "theme: Light",
            await File.ReadAllTextAsync(Path.Combine(backupPath, "app.yaml"), TestContext.Current.CancellationToken),
            StringComparison.Ordinal);
        AssertNoRestoreResidue();
    }

    [Fact]
    public async Task CreatePackageAsync_ExcludesInFlightConfigurationTransactionArtifacts()
    {
        var serversDirectory = Path.Combine(_appData.ConfigRootPath, "servers");
        Directory.CreateDirectory(serversDirectory);
        var profilePath = Path.Combine(serversDirectory, "agent.yaml");
        await File.WriteAllTextAsync(profilePath, "schema_version: 2\nid: agent\n", TestContext.Current.CancellationToken);
        // Recovery material from an interrupted local save belongs to this device, not to the package.
        await File.WriteAllTextAsync(
            profilePath + ConfigurationFileTransactionArtifacts.PendingSuffix + "abc",
            "schema_version: 2\nid: agent-candidate\n",
            TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            profilePath + ConfigurationFileTransactionArtifacts.RollbackSuffix + "def",
            "schema_version: 2\nid: agent-previous\n",
            TestContext.Current.CancellationToken);

        var package = await _packageService.CreatePackageAsync(
            includeSecrets: false,
            TestContext.Current.CancellationToken);

        using var stream = new MemoryStream(package, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var entryNames = archive.Entries.Select(entry => entry.FullName).ToList();
        Assert.Contains("files/config/servers/agent.yaml", entryNames);
        Assert.DoesNotContain(
            entryNames,
            name => name.Contains(ConfigurationFileTransactionArtifacts.PendingSuffix, StringComparison.Ordinal)
                || name.Contains(ConfigurationFileTransactionArtifacts.RollbackSuffix, StringComparison.Ordinal));
    }

    [Fact]
    public async Task CreatePackageAsync_ManifestFilesUsePortableSlashSeparators()
    {
        var serversDirectory = Path.Combine(_appData.ConfigRootPath, "servers");
        Directory.CreateDirectory(serversDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(serversDirectory, "agent.yaml"),
            "schema_version: 2\nid: agent\n",
            TestContext.Current.CancellationToken);

        var package = await _packageService.CreatePackageAsync(
            includeSecrets: false,
            TestContext.Current.CancellationToken);

        using var stream = new MemoryStream(package, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var manifestEntry = archive.GetEntry("manifest.json");
        Assert.NotNull(manifestEntry);
        using var reader = new StreamReader(manifestEntry!.Open());
        var manifest = System.Text.Json.JsonDocument.Parse(reader.ReadToEnd());
        var files = manifest.RootElement.GetProperty("files").EnumerateArray()
            .Select(value => value.GetString())
            .ToList();
        // 便携包元数据的路径契约与 zip 条目一致：恒定 '/'（相对 config root），不随平台分隔符漂移。
        Assert.Contains("servers/agent.yaml", files);
        Assert.All(files, file => Assert.False(file!.Contains('\\', StringComparison.Ordinal)));
    }

    [Fact]
    public async Task CreatePackageAsync_ExcludesConfigurationRecoveryDirectory()
    {
        Directory.CreateDirectory(Path.Combine(_appData.ConfigRootPath, "recovery"));
        await File.WriteAllTextAsync(
            Path.Combine(_appData.ConfigRootPath, "recovery", "pending.journal.json"),
            "{\"profileId\":\"agent\"}",
            TestContext.Current.CancellationToken);

        var package = await _packageService.CreatePackageAsync(
            includeSecrets: false,
            TestContext.Current.CancellationToken);

        using var stream = new MemoryStream(package, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        Assert.DoesNotContain(archive.Entries, entry => entry.FullName.Contains("recovery", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RestorePackageAsync_WhenConfigRootDeleteFailsMidway_RestoresConfigRootFromBackupAndRethrowsOriginal()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "只读文件仅在 Windows 上阻断递归删除。");
        Directory.CreateDirectory(_appData.ConfigRootPath);
        var appYamlPath = Path.Combine(_appData.ConfigRootPath, "app.yaml");
        var readOnlyPath = Path.Combine(_appData.ConfigRootPath, "pinned.yaml");
        await File.WriteAllTextAsync(appYamlPath, "schema_version: 2\ntheme: Light\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(readOnlyPath, "pinned: true", TestContext.Current.CancellationToken);
        File.SetAttributes(readOnlyPath, FileAttributes.ReadOnly);

        // 只读文件让 config root 的递归删除中途失败;原始异常必须原样上抛(不得被包裹),
        // 且 config root 必须已从 backup 恢复为换入前内容。
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _packageService.RestorePackageAsync(CreateRemotePackage("theme: Dark"), TestContext.Current.CancellationToken));

        Assert.Contains(
            "theme: Light",
            await File.ReadAllTextAsync(appYamlPath, TestContext.Current.CancellationToken),
            StringComparison.Ordinal);
        Assert.Equal("pinned: true", await File.ReadAllTextAsync(readOnlyPath, TestContext.Current.CancellationToken));
        AssertNoRestoreResidue();
    }

    [Fact]
    public async Task RestorePackageAsync_WhenConfigRootSwapMoveFails_RethrowsOriginalAndPreservesLocalState()
    {
        Directory.CreateDirectory(_appData.AppDataRootPath);
        // config root 位置被一个同名文件占据:删除分支不命中(不是目录),staging 换入的 Move 必然失败。
        await File.WriteAllTextAsync(_appData.ConfigRootPath, "not-a-directory", TestContext.Current.CancellationToken);

        await Assert.ThrowsAnyAsync<IOException>(() =>
            _packageService.RestorePackageAsync(CreateRemotePackage("theme: Dark"), TestContext.Current.CancellationToken));

        Assert.Equal(
            "not-a-directory",
            await File.ReadAllTextAsync(_appData.ConfigRootPath, TestContext.Current.CancellationToken));
        AssertNoRestoreResidue();
    }

    [Fact]
    public async Task RestorePackageAsync_WhenRollbackFails_ThrowsAggregateExposingOriginalFailureAndBackupPath()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "被占用的文件句柄仅在 Windows 上阻断删除。");
        Directory.CreateDirectory(_appData.ConfigRootPath);
        var heldPath = Path.Combine(_appData.ConfigRootPath, "held.yaml");
        await File.WriteAllTextAsync(heldPath, "held: true", TestContext.Current.CancellationToken);
        // 不带 FileShare.Delete 的句柄让换入删除与回滚清理先后失败。
        using var blockingHandle = new FileStream(heldPath, FileMode.Open, FileAccess.Read, FileShare.Read);

        var aggregate = await Assert.ThrowsAsync<AggregateException>(() =>
            _packageService.RestorePackageAsync(CreateRemotePackage("theme: Dark"), TestContext.Current.CancellationToken));

        // 原始换入异常与回滚异常都必须可见,原始异常在前。
        Assert.Equal(2, aggregate.InnerExceptions.Count);
        Assert.IsAssignableFrom<IOException>(aggregate.InnerExceptions[0]);
        var backupRoot = Path.Combine(_appData.AppDataRootPath, "config-backups");
        Assert.Contains(backupRoot, aggregate.Message, StringComparison.Ordinal);
        // 消息里指向的 backup 必须真实存在并包含换入前内容,用户可手动恢复。
        var backupDirectory = Assert.Single(Directory.EnumerateDirectories(backupRoot));
        Assert.Equal(
            "held: true",
            await File.ReadAllTextAsync(Path.Combine(backupDirectory, "held.yaml"), TestContext.Current.CancellationToken));
        AssertNoRestoreResidue();
    }

    private void AssertNoRestoreResidue()
    {
        // 成功与失败路径都不得在 appdata 根下残留 staging/临时目录。
        var unexpected = Directory.EnumerateDirectories(_appData.AppDataRootPath)
            .Select(Path.GetFileName)
            .Where(name => name is not ("config" or "config-backups"))
            .ToList();
        Assert.Empty(unexpected);
    }

    private static byte[] CreateRemotePackage(string appYaml)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(
                archive,
                "manifest.json",
                """{"schemaVersion":1,"appId":"SalmonEgg","createdAtUtc":"2026-01-01T00:00:00.0000000Z","files":["app.yaml"]}""");
            WriteEntry(archive, "files/config/app.yaml", $"schema_version: 2{Environment.NewLine}{appYaml}{Environment.NewLine}");
        }

        return stream.ToArray();
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }
}
