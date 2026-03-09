using System;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models.Diagnostics;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Infrastructure.Services;

public sealed class DiagnosticsBundleService : IDiagnosticsBundleService
{
    private readonly IAppDataService _paths;

    public DiagnosticsBundleService(IAppDataService paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    public async Task<string> CreateBundleAsync(DiagnosticsSnapshot snapshot)
    {
        if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));

        Directory.CreateDirectory(_paths.ExportsDirectoryPath);

        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
        var zipPath = Path.Combine(_paths.ExportsDirectoryPath, $"diagnostics-{timestamp}.zip");

        var tempDir = Path.Combine(_paths.ExportsDirectoryPath, $"diagnostics-{timestamp}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var snapshotPath = Path.Combine(tempDir, "snapshot.json");
            var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(snapshotPath, json).ConfigureAwait(false);

            CopyIfExists(Path.Combine(_paths.AppDataRootPath, "boot.log"), Path.Combine(tempDir, "boot.log"));
            CopyDirectoryIfExists(_paths.LogsDirectoryPath, Path.Combine(tempDir, "logs"));
            CopyDirectoryIfExists(_paths.ConfigRootPath, Path.Combine(tempDir, "config"));
            CopyDirectoryIfExists(_paths.CacheRootPath, Path.Combine(tempDir, "cache"));

            if (File.Exists(zipPath))
            {
                File.Delete(zipPath);
            }

            ZipFile.CreateFromDirectory(tempDir, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
            return zipPath;
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
            catch
            {
            }
        }
    }

    private static void CopyIfExists(string source, string destination)
    {
        try
        {
            if (!File.Exists(source))
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: true);
        }
        catch
        {
        }
    }

    private static void CopyDirectoryIfExists(string sourceDir, string destinationDir)
    {
        try
        {
            if (!Directory.Exists(sourceDir))
            {
                return;
            }

            Directory.CreateDirectory(destinationDir);
            foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(sourceDir, file);
                var dest = Path.Combine(destinationDir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(file, dest, overwrite: true);
            }
        }
        catch
        {
        }
    }
}
