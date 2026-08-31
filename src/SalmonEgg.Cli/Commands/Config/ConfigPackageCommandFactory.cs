using System;
using System.CommandLine;

namespace SalmonEgg.Cli.Commands.Config;

/// <summary>
/// Constructs the <c>config validate</c>, <c>config export</c>, and <c>config import</c> commands.
/// </summary>
public static class ConfigPackageCommandFactory
{
    public static Command CreateValidateCommand(ConfigPackageHandler handler)
    {
        if (handler is null) throw new ArgumentNullException(nameof(handler));

        var cmd = new Command("validate", "Check configuration files for schema and parse problems.");
        cmd.SetAction((_, ct) => handler.ValidateAsync(ct));
        return cmd;
    }

    public static Command CreateExportCommand(ConfigPackageHandler handler)
    {
        var secretsOpt = new Option<bool>("--include-secrets")
        {
            Description = "Embed credentials in the package. The package then contains secret material; store it accordingly."
        };

        var cmd = new Command("export", "Export the current configuration as a portable package (zip).");
        cmd.Options.Add(secretsOpt);
        cmd.SetAction((parseResult, ct) => handler.ExportAsync(parseResult.GetValue(secretsOpt), ct));
        return cmd;
    }

    public static Command CreateImportCommand(ConfigPackageHandler handler)
    {
        var pathArg = new Argument<string>("package-path") { Description = "Path to a previously exported configuration package (zip)." };

        var cmd = new Command("import", "Import a configuration package, replacing the current configuration (a backup is kept).");
        cmd.Arguments.Add(pathArg);
        cmd.SetAction((parseResult, ct) => handler.ImportAsync(parseResult.GetRequiredValue(pathArg), ct));
        return cmd;
    }
}
