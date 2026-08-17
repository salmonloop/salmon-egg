using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using SalmonEgg.Cli.Commands.Config;
using SalmonEgg.Cli.Commands.Credentials;
using SalmonEgg.Cli.Hosting;

namespace SalmonEgg.Cli.Commands;

/// <summary>
/// Owns the CLI command hierarchy and its public command names.
/// </summary>
/// <remarks>
/// Command factories own only the public command tree and parser binding. Business operations are
/// delegated to constructor-injected handlers, keeping command-line concerns out of the domain and
/// infrastructure layers.
/// </remarks>
public sealed class CliCommandFactory
{
    private const string RootDescription = "Salmon Egg configuration management CLI";
    private const string ConfigDescription = "Manage configuration (servers, app settings, packages).";
    private readonly CliVersionProvider _versionProvider;
    private readonly ServerConfigurationHandler _serverConfigurationHandler;
    private readonly CredentialsHandler _credentialsHandler;

    public CliCommandFactory(
        CliVersionProvider versionProvider,
        ServerConfigurationHandler serverConfigurationHandler,
        CredentialsHandler credentialsHandler)
    {
        _versionProvider = versionProvider ?? throw new ArgumentNullException(nameof(versionProvider));
        _serverConfigurationHandler = serverConfigurationHandler ?? throw new ArgumentNullException(nameof(serverConfigurationHandler));
        _credentialsHandler = credentialsHandler ?? throw new ArgumentNullException(nameof(credentialsHandler));
    }

    /// <summary>
    /// Creates the root command and the structural command groups.
    /// </summary>
    public RootCommand CreateRootCommand()
    {
        var root = new RootCommand(RootDescription);
        root.Children.OfType<VersionOption>().Single().Action = new PrintVersionAction(_versionProvider);
        root.SetAction(_ => CliExitCodes.Success);

        var config = CreateStructuralCommand("config", ConfigDescription);
        config.Subcommands.Add(ConfigServerCommandFactory.CreateServerCommand(_serverConfigurationHandler));
        var credentials = CredentialsCommandFactory.CreateCredentialsNamespaceCommand();

        root.Subcommands.Add(config);
        root.Subcommands.Add(credentials);
        root.Subcommands.Add(CredentialsCommandFactory.CreateSetCredentialCommand(_credentialsHandler));
        root.Subcommands.Add(CredentialsCommandFactory.CreateClearCredentialCommand(_credentialsHandler));
        root.Subcommands.Add(CredentialsCommandFactory.CreateHasCredentialCommand(_credentialsHandler));
        return root;
    }

    private static Command CreateStructuralCommand(string name, string description)
    {
        var command = new Command(name, description);
        command.SetAction(parseResult =>
        {
            var output = parseResult.InvocationConfiguration.Output;
            output.WriteLine($"{description}");
            output.WriteLine();
            output.WriteLine($"Usage:");
            output.WriteLine($"  salmon-egg {name} [options]");
            output.WriteLine();
            output.WriteLine($"Run 'salmon-egg {name} --help' for available commands.");
            return CliExitCodes.Success;
        });
        return command;
    }

    private sealed class PrintVersionAction : SynchronousCommandLineAction
    {
        private readonly CliVersionProvider _versionProvider;

        public PrintVersionAction(CliVersionProvider versionProvider)
        {
            _versionProvider = versionProvider ?? throw new ArgumentNullException(nameof(versionProvider));
        }

        public override int Invoke(ParseResult parseResult)
        {
            parseResult.InvocationConfiguration.Output.WriteLine(_versionProvider.Version);
            return CliExitCodes.Success;
        }
    }
}
