using System;
using System.CommandLine;

namespace SalmonEgg.Cli.Commands.Config;

/// <summary>
/// Constructs the <c>config settings</c> command subtree.
/// </summary>
/// <remarks>
/// Same split as <see cref="ConfigServerCommandFactory"/>: this factory owns only command
/// structure and parser binding; business logic lives in <see cref="AppSettingsHandler"/>,
/// supplied by the composition root.
/// </remarks>
public static class ConfigSettingsCommandFactory
{
    public static Command CreateSettingsCommand(AppSettingsHandler handler)
    {
        if (handler is null) throw new ArgumentNullException(nameof(handler));

        var settings = new Command("settings", "View or update global app settings (app.yaml).");

        settings.Subcommands.Add(CreateGetCommand(handler));
        settings.Subcommands.Add(CreateSetCommand(handler));

        return settings;
    }

    private static Command CreateGetCommand(AppSettingsHandler handler)
    {
        var cmd = new Command("get", "Show all editable global settings with their current values.");
        cmd.SetAction((_, ct) => handler.GetAsync(ct));
        return cmd;
    }

    private static Command CreateSetCommand(AppSettingsHandler handler)
    {
        var keyArg = new Argument<string>("key") { Description = "Setting key, for example theme or cache_retention_days." };
        var valueArg = new Argument<string>("value") { Description = "New value for the setting." };

        var cmd = new Command("set", "Update one global setting field.");
        cmd.Arguments.Add(keyArg);
        cmd.Arguments.Add(valueArg);
        cmd.SetAction((parseResult, ct) => handler.SetAsync(
            parseResult.GetRequiredValue(keyArg),
            parseResult.GetRequiredValue(valueArg),
            ct));
        return cmd;
    }
}
