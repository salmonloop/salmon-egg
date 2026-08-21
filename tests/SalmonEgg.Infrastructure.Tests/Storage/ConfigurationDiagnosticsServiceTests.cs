using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SalmonEgg.Infrastructure.Storage;

namespace SalmonEgg.Infrastructure.Tests.Storage;

/// <summary>
/// Tests for the read-only configuration diagnostics view.
/// </summary>
public sealed class ConfigurationDiagnosticsServiceTests : IDisposable
{
    private readonly string _testDirectory;

    public ConfigurationDiagnosticsServiceTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "SalmonEggConfigDiagnosticsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDirectory);
        Environment.SetEnvironmentVariable("SALMONEGG_APPDATA_ROOT", Path.Combine(_testDirectory, "SalmonEgg"), EnvironmentVariableTarget.Process);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("SALMONEGG_APPDATA_ROOT", null, EnvironmentVariableTarget.Process);
        try
        {
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup failures.
        }
    }

    [Fact]
    public async Task InspectAsync_WithHealthyFiles_ReportsOkVersions()
    {
        WriteAppYaml("schema_version: 3\ntheme: Dark\n");
        WriteServerYaml("alpha", "schema_version: 2\nid: alpha\nname: Alpha\ntransport: websocket\nserver_url: ws://a\n");

        var service = CreateService();
        var results = await service.InspectAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, results.Count);
        var app = Assert.Single(results, r => r.FileName == "app.yaml");
        Assert.Equal(ConfigurationDiagnosticKind.Ok, app.Kind);
        Assert.Equal(3, app.SchemaVersion);
        var server = Assert.Single(results, r => r.FileName == Path.Combine("servers", "alpha.yaml"));
        Assert.Equal(ConfigurationDiagnosticKind.Ok, server.Kind);
        Assert.Equal(2, server.SchemaVersion);
    }

    [Fact]
    public async Task InspectAsync_WithoutConfigs_ReportsAbsentAppYamlOnly()
    {
        var service = CreateService();

        var results = await service.InspectAsync(TestContext.Current.CancellationToken);

        var app = Assert.Single(results);
        Assert.Equal(ConfigurationDiagnosticKind.Absent, app.Kind);
    }

    [Fact]
    public async Task InspectAsync_WithTooNewAndCorruptFiles_ReportsBothKinds()
    {
        WriteAppYaml("schema_version: 50\ntheme: Dark\n");
        WriteServerYaml("broken", "schema_version: 2\nname: [unclosed\nsecret_value: do-not-echo\n");

        var service = CreateService();
        var results = await service.InspectAsync(TestContext.Current.CancellationToken);

        var tooNew = Assert.Single(results, r => r.Kind == ConfigurationDiagnosticKind.SchemaTooNew);
        Assert.Equal("app.yaml", tooNew.FileName);
        Assert.Equal(50, tooNew.SchemaVersion);
        Assert.Contains("upgrade", tooNew.Detail, StringComparison.OrdinalIgnoreCase);

        var unparsable = Assert.Single(results, r => r.Kind == ConfigurationDiagnosticKind.Unparsable);
        Assert.Contains("broken", unparsable.FileName, StringComparison.Ordinal);
        // 诊断不得回显文件内容：Detail 只含位置与异常类别。
        Assert.DoesNotContain(unparsable.Detail!, "do-not-echo", StringComparison.Ordinal);
        Assert.DoesNotContain(unparsable.Detail!, "unclosed", StringComparison.Ordinal);
        Assert.StartsWith("YAML parse failed at line", unparsable.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void InspectPackage_WithMixedEntries_AppliesPerFileSchemaThresholds()
    {
        var package = BuildPackage(
            ("manifest.json", """{"schemaVersion":1,"appId":"SalmonEgg","includesSecrets":false,"files":[]}"""),
            ("files/config/app.yaml", "schema_version: 3\ntheme: Dark\n"),
            ("files/config/servers/current.yaml", "schema_version: 2\nid: current\nname: Current\ntransport: websocket\nserver_url: ws://c\n"),
            ("files/config/servers/future.yaml", "schema_version: 90\nid: future\nname: Future\ntransport: websocket\nserver_url: ws://f\n"));

        var service = CreateService();
        var results = service.InspectPackage(package);

        Assert.Equal(3, results.Count);
        Assert.All(results.Where(r => r.FileName != "servers/future.yaml"), r => Assert.Equal(ConfigurationDiagnosticKind.Ok, r.Kind));
        var future = Assert.Single(results, r => r.FileName == "servers/future.yaml");
        Assert.Equal(ConfigurationDiagnosticKind.SchemaTooNew, future.Kind);
        Assert.Equal(90, future.SchemaVersion);
    }

    [Fact]
    public void InspectPackage_WithMissingManifest_ReportsUnidentifiablePackage()
    {
        var package = BuildPackage(("files/config/app.yaml", "schema_version: 3\n"));

        var service = CreateService();
        var results = service.InspectPackage(package);

        var manifest = Assert.Single(results);
        Assert.Equal("manifest.json", manifest.FileName);
        Assert.Equal(ConfigurationDiagnosticKind.Unparsable, manifest.Kind);
        Assert.Contains("manifest.json", manifest.Detail, StringComparison.Ordinal);
    }

    private ConfigurationDiagnosticsService CreateService() =>
        new(new AppDataService(), new FileSystemAppFileStore());

    private void WriteAppYaml(string yaml)
    {
        var path = Path.Combine(_testDirectory, "SalmonEgg", "config", "app.yaml");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, yaml);
    }

    private void WriteServerYaml(string id, string yaml)
    {
        var path = Path.Combine(_testDirectory, "SalmonEgg", "config", "servers", $"{id}.yaml");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, yaml);
    }

    private static byte[] BuildPackage(params (string EntryName, string Content)[] entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (entryName, content) in entries)
            {
                var entry = archive.CreateEntry(entryName);
                using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
                writer.Write(content);
            }
        }

        return stream.ToArray();
    }
}
