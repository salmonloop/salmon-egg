using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Cli.Output;
using SalmonEgg.Domain.Services;
using SalmonEgg.Infrastructure.Storage;

namespace SalmonEgg.Cli.Commands.Config;

/// <summary>
/// Implements the <c>config validate</c>, <c>config export</c>, and <c>config import</c> operations.
/// </summary>
/// <remarks>
/// Validation reports what the shared diagnostics service sees; export/import delegate entirely to
/// <see cref="ConfigSyncPackageService"/> so the CLI never re-implements packaging. Import refuses
/// packages whose files carry a schema version newer than this build supports — the same
/// refuse-write-back rule the services enforce on individual writes, applied before anything is
/// swapped into the config root.
/// </remarks>
public sealed class ConfigPackageHandler
{
    private readonly ICliOutput _output;
    private readonly ConfigurationDiagnosticsService _diagnostics;
    private readonly ConfigSyncPackageService _packages;
    private readonly IAppDataService _appData;

    public ConfigPackageHandler(
        ICliOutput output,
        ConfigurationDiagnosticsService diagnostics,
        ConfigSyncPackageService packages,
        IAppDataService appData)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _packages = packages ?? throw new ArgumentNullException(nameof(packages));
        _appData = appData ?? throw new ArgumentNullException(nameof(appData));
    }

    /// <summary>
    /// Validates every configuration file and prints one diagnostic line per file.
    /// </summary>
    public async Task<int> ValidateAsync(CancellationToken cancellationToken)
    {
        var results = await _diagnostics.InspectAsync(cancellationToken).ConfigureAwait(false);
        var failures = 0;
        foreach (var result in results)
        {
            switch (result.Kind)
            {
                case ConfigurationDiagnosticKind.Ok:
                    await _output.WriteAsync(
                        $"{result.FileName}: ok (schema_version {result.SchemaVersion}).").ConfigureAwait(false);
                    break;
                case ConfigurationDiagnosticKind.Absent:
                    await _output.WriteAsync($"{result.FileName}: not present (defaults apply).").ConfigureAwait(false);
                    break;
                case ConfigurationDiagnosticKind.SchemaTooNew:
                    failures++;
                    await _output.WriteErrorAsync($"{result.FileName}: {result.Detail}").ConfigureAwait(false);
                    break;
                case ConfigurationDiagnosticKind.Unparsable:
                    failures++;
                    await _output.WriteErrorAsync(
                        $"{result.FileName}: YAML could not be parsed ({result.Detail}). "
                        + "The file is treated as defaults until it is repaired.").ConfigureAwait(false);
                    break;
            }
        }

        return failures == 0 ? CliExitCodes.Success : CliExitCodes.Failure;
    }

    /// <summary>
    /// Exports a configuration package into the exports directory.
    /// </summary>
    public async Task<int> ExportAsync(bool includeSecrets, CancellationToken cancellationToken)
    {
        var bytes = await _packages.CreatePackageAsync(includeSecrets, cancellationToken).ConfigureAwait(false);

        Directory.CreateDirectory(_appData.ExportsDirectoryPath);
        var fileName = $"salmon-egg-config-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.zip";
        var destination = Path.Combine(_appData.ExportsDirectoryPath, fileName);
        await File.WriteAllBytesAsync(destination, bytes, cancellationToken).ConfigureAwait(false);

        await _output.WriteAsync($"Exported configuration package: {destination}").ConfigureAwait(false);
        if (!includeSecrets)
        {
            await _output.WriteAsync("Secrets were not included; pass --include-secrets to embed credentials.").ConfigureAwait(false);
        }

        return CliExitCodes.Success;
    }

    /// <summary>
    /// Imports a configuration package after refusing packages this build cannot safely write back.
    /// </summary>
    public async Task<int> ImportAsync(string packagePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(packagePath))
        {
            await _output.WriteErrorAsync($"Package not found: {packagePath}").ConfigureAwait(false);
            return CliExitCodes.Usage;
        }

        var package = await File.ReadAllBytesAsync(packagePath, cancellationToken).ConfigureAwait(false);
        var packageResults = _diagnostics.InspectPackage(package);
        var refusals = packageResults.Where(result =>
            result.Kind is ConfigurationDiagnosticKind.SchemaTooNew or ConfigurationDiagnosticKind.Unparsable).ToList();
        if (refusals.Count > 0)
        {
            foreach (var refusal in refusals)
            {
                await _output.WriteErrorAsync($"{refusal.FileName}: {refusal.Detail}").ConfigureAwait(false);
            }

            await _output.WriteErrorAsync(
                "Refusing to import. Upgrade Salmon Egg to a version that supports these files, migrate there, then import here.").ConfigureAwait(false);
            return CliExitCodes.Failure;
        }

        var backupPath = await _packages.RestorePackageAsync(package, cancellationToken).ConfigureAwait(false);
        await _output.WriteAsync($"Imported configuration from {packagePath}.").ConfigureAwait(false);
        await _output.WriteAsync($"Previous configuration backed up at: {backupPath}").ConfigureAwait(false);
        return CliExitCodes.Success;
    }
}
