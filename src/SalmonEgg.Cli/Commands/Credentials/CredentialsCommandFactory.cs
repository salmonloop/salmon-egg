using System.CommandLine;

namespace SalmonEgg.Cli.Commands.Credentials;

internal static class CredentialsCommandFactory
{
    private const string CredentialsDescription = "Credential commands are available as set-credential, clear-credential, and has-credential.";

    public static Command CreateCredentialsNamespaceCommand()
    {
        var credentials = new Command("credentials") { Description = CredentialsDescription };
        credentials.SetAction(parseResult =>
        {
            var output = parseResult.InvocationConfiguration.Output;
            output.WriteLine(CredentialsDescription);
            output.WriteLine();
            output.WriteLine("Usage:");
            output.WriteLine("  salmon-egg set-credential <server-id> --token <value>");
            output.WriteLine("  salmon-egg clear-credential <server-id>");
            output.WriteLine("  salmon-egg has-credential <server-id>");
            output.WriteLine();
            output.WriteLine("Run 'salmon-egg credentials --help' for this namespace guidance.");
            return CliExitCodes.Success;
        });
        return credentials;
    }

    public static Command CreateSetCredentialCommand(CredentialsHandler handler)
    {
        if (handler is null) throw new ArgumentNullException(nameof(handler));

        var serverIdArg = new Argument<string>("server-id") { Description = "The server identifier" };
        var tokenOpt = new Option<string?>("--token") { Description = "Bearer token to store." };
        var apiKeyOpt = new Option<string?>("--api-key") { Description = "API key to store." };
        var cmd = new Command("set-credential") { Description = "Store a token or API key for a configured server." };
        cmd.Arguments.Add(serverIdArg);
        cmd.Options.Add(tokenOpt);
        cmd.Options.Add(apiKeyOpt);
        cmd.SetAction((parseResult, ct) => handler.SetAsync(
            parseResult.GetRequiredValue(serverIdArg),
            parseResult.GetValue(tokenOpt),
            parseResult.GetValue(apiKeyOpt),
            ct));
        return cmd;
    }

    public static Command CreateClearCredentialCommand(CredentialsHandler handler)
    {
        if (handler is null) throw new ArgumentNullException(nameof(handler));

        var serverIdArg = new Argument<string>("server-id") { Description = "The server identifier" };
        var cmd = new Command("clear-credential") { Description = "Clear all credentials for a configured server." };
        cmd.Arguments.Add(serverIdArg);
        cmd.SetAction((parseResult, ct) => handler.ClearAsync(parseResult.GetRequiredValue(serverIdArg), ct));
        return cmd;
    }

    public static Command CreateHasCredentialCommand(CredentialsHandler handler)
    {
        if (handler is null) throw new ArgumentNullException(nameof(handler));

        var serverIdArg = new Argument<string>("server-id") { Description = "The server identifier" };
        var cmd = new Command("has-credential") { Description = "Check whether a configured server has stored credentials." };
        cmd.Arguments.Add(serverIdArg);
        cmd.SetAction((parseResult, ct) => handler.HasAsync(parseResult.GetRequiredValue(serverIdArg), ct));
        return cmd;
    }
}
