using System;
using System.Collections.Generic;
using System.Linq;
using System.CommandLine;
using SalmonEgg.Domain.Models;

namespace SalmonEgg.Cli.Commands.Config;

/// <summary>
/// Constructs the <c>config server</c> command subtree.
/// </summary>
/// <remarks>
/// This factory owns only command structure (names, options, help text) and the binding from
/// parsed values to handler method calls. All business logic lives in
/// <see cref="ServerConfigurationHandler"/>, which is supplied by the caller (constructor
/// injection at the composition root) rather than resolved from a service locator here.
/// </remarks>
public static class ConfigServerCommandFactory
{
    public static Command CreateServerCommand(
        ServerConfigurationHandler handler,
        IReadOnlyList<string>? rawArgs = null)
    {
        if (handler is null) throw new ArgumentNullException(nameof(handler));

        var server = new Command("server", "Manage ACP server configurations.");

        server.Subcommands.Add(CreateListCommand(handler));
        server.Subcommands.Add(CreateShowCommand(handler));
        server.Subcommands.Add(CreateAddCommand(handler, rawArgs));
        server.Subcommands.Add(CreateUpdateCommand(handler, rawArgs));
        server.Subcommands.Add(CreateRemoveCommand(handler));

        return server;
    }

    // ── list ──────────────────────────────────────────────────────────────────

    private static Command CreateListCommand(ServerConfigurationHandler handler)
    {
        var cmd = new Command("list", "List all server configurations.");
        cmd.SetAction((_, ct) => handler.ListAsync(ct));
        return cmd;
    }

    // ── show ──────────────────────────────────────────────────────────────────

    private static Command CreateShowCommand(ServerConfigurationHandler handler)
    {
        var idArg = new Argument<string>("id") { Description = "Server configuration ID." };
        var cmd = new Command("show", "Show a server configuration.");
        cmd.Arguments.Add(idArg);
        cmd.SetAction((parseResult, ct) => handler.ShowAsync(parseResult.GetRequiredValue(idArg), ct));
        return cmd;
    }

    // ── add ───────────────────────────────────────────────────────────────────

    private static Command CreateAddCommand(ServerConfigurationHandler handler, IReadOnlyList<string>? rawArgs)
    {
        var nameOpt = new Option<string>("--name") { Description = "Display name.", Required = true };
        var urlOpt = new Option<string?>("--url") { Description = "WebSocket or HTTP endpoint URL." };
        var transportOpt = CreateTransportOption();
        var stdioCommandOpt = new Option<string?>("--stdio-command") { Description = "Command for stdio transport." };
        var stdioArgsOpt = CreateStdioArgsOption("Arguments for the stdio command.");
        var timeoutOpt = CreateTimeoutOption();
        var authenticationOptions = CreateAuthenticationOptions();
        var proxyOptions = CreateProxyOptions();

        var cmd = new Command("add", "Add a new server configuration.");
        cmd.Options.Add(nameOpt);
        cmd.Options.Add(urlOpt);
        cmd.Options.Add(transportOpt);
        cmd.Options.Add(stdioCommandOpt);
        cmd.Options.Add(stdioArgsOpt);
        cmd.Options.Add(timeoutOpt);
        AddOptions(cmd, authenticationOptions, proxyOptions);

        cmd.SetAction((parseResult, ct) => handler.AddFromStdinAsync(
            parseResult.GetRequiredValue(nameOpt),
            parseResult.GetValue(urlOpt),
            ParseTransport(parseResult.GetValue(transportOpt)!),
            parseResult.GetValue(stdioCommandOpt),
            ParseStdioArguments(parseResult, stdioArgsOpt, rawArgs),
            parseResult.GetValue(timeoutOpt),
            CreatePatch(parseResult, authenticationOptions, proxyOptions),
            parseResult.GetValue(authenticationOptions.Token),
            parseResult.GetValue(authenticationOptions.ApiKey),
            ct));
        return cmd;
    }

    private static void AddOptions(
        Command command,
        AuthenticationOptions authenticationOptions,
        ProxyOptions proxyOptions)
    {
        command.Options.Add(authenticationOptions.Token);
        command.Options.Add(authenticationOptions.ApiKey);
        command.Options.Add(authenticationOptions.Mode);
        command.Options.Add(proxyOptions.Mode);
        command.Options.Add(proxyOptions.Url);
    }

    private static ServerConfigurationPatch CreatePatch(
        ParseResult parseResult,
        AuthenticationOptions authenticationOptions,
        ProxyOptions proxyOptions)
    {
        var tokenResult = parseResult.GetResult(authenticationOptions.Token);
        var apiKeyResult = parseResult.GetResult(authenticationOptions.ApiKey);
        var authModeResult = parseResult.GetResult(authenticationOptions.Mode);
        var proxyModeResult = parseResult.GetResult(proxyOptions.Mode);
        var proxyUrlResult = parseResult.GetResult(proxyOptions.Url);

        return new ServerConfigurationPatch(
            Token: null,
            ApiKey: null,
            AuthenticationSpecified: tokenResult is not null || apiKeyResult is not null || authModeResult is not null,
            AuthenticationMode: parseResult.GetValue(authenticationOptions.Mode),
            ProxySpecified: proxyModeResult is not null,
            ProxyMode: parseResult.GetValue(proxyOptions.Mode),
            ProxyUrlSpecified: proxyUrlResult is not null,
            ProxyUrl: parseResult.GetValue(proxyOptions.Url));
    }

    private static AuthenticationOptions CreateAuthenticationOptions() => new(
        new Option<bool>("--token-stdin") { Description = "Read the bearer token from stdin (one line)." },
        new Option<bool>("--api-key-stdin") { Description = "Read the API key from stdin (one line)." },
        new Option<string?>("--auth") { Description = "Authentication mode: none, bearer_token, api_key." });

    private static ProxyOptions CreateProxyOptions() => new(
        new Option<ProxyMode?>("--proxy-mode") { Description = "Proxy mode: none, system, custom." },
        new Option<string?>("--proxy-url") { Description = "Custom proxy URL (requires --proxy-mode custom)." });

    private sealed record AuthenticationOptions(
        Option<bool> Token,
        Option<bool> ApiKey,
        Option<string?> Mode);

    private sealed record ProxyOptions(
        Option<ProxyMode?> Mode,
        Option<string?> Url);

    // ── update ────────────────────────────────────────────────────────────────

    private static Command CreateUpdateCommand(ServerConfigurationHandler handler, IReadOnlyList<string>? rawArgs)
    {
        var idArg = new Argument<string>("id") { Description = "Server configuration ID to update." };
        var nameOpt = new Option<string?>("--name") { Description = "New display name." };
        var urlOpt = new Option<string?>("--url") { Description = "New WebSocket or HTTP endpoint URL." };
        var transportOpt = CreateOptionalTransportOption();
        var stdioCommandOpt = new Option<string?>("--stdio-command") { Description = "New command for stdio transport." };
        var stdioArgsOpt = CreateStdioArgsOption("New arguments for the stdio command. Use an empty string to clear.");
        var timeoutOpt = CreateTimeoutOption();
        var authenticationOptions = CreateAuthenticationOptions();
        var proxyOptions = CreateProxyOptions();

        var cmd = new Command("update", "Update fields of an existing server configuration.");
        cmd.Arguments.Add(idArg);
        cmd.Options.Add(nameOpt);
        cmd.Options.Add(urlOpt);
        cmd.Options.Add(transportOpt);
        cmd.Options.Add(stdioCommandOpt);
        cmd.Options.Add(stdioArgsOpt);
        cmd.Options.Add(timeoutOpt);
        AddOptions(cmd, authenticationOptions, proxyOptions);

        cmd.SetAction((parseResult, ct) => handler.UpdateFromStdinAsync(
            parseResult.GetRequiredValue(idArg),
            parseResult.GetValue(nameOpt),
            parseResult.GetValue(urlOpt),
            ParseOptionalTransport(parseResult.GetValue(transportOpt)),
            parseResult.GetValue(stdioCommandOpt),
            // Omitted arguments preserve the stored value; an explicitly empty string clears it.
            parseResult.GetResult(stdioArgsOpt) is null ? null : ParseStdioArguments(parseResult, stdioArgsOpt, rawArgs),
            parseResult.GetValue(timeoutOpt),
            CreatePatch(parseResult, authenticationOptions, proxyOptions),
            parseResult.GetValue(authenticationOptions.Token),
            parseResult.GetValue(authenticationOptions.ApiKey),
            ct));
        return cmd;
    }

    // ── remove ────────────────────────────────────────────────────────────────

    private static Command CreateRemoveCommand(ServerConfigurationHandler handler)
    {
        var idArg = new Argument<string>("id") { Description = "Server configuration ID to remove." };
        var yesOpt = new Option<bool>("--yes") { Description = "Confirm removal without interactive prompt." };

        var cmd = new Command("remove", "Remove a server configuration and its credentials.");
        cmd.Arguments.Add(idArg);
        cmd.Options.Add(yesOpt);

        cmd.SetAction((parseResult, ct) => handler.RemoveAsync(
            parseResult.GetRequiredValue(idArg),
            parseResult.GetValue(yesOpt),
            ct));
        return cmd;
    }

    // ── shared options ────────────────────────────────────────────────────────

    /// <summary>
    /// Creates the stdio arguments option.
    /// </summary>
    private static Option<string?> CreateStdioArgsOption(string description) =>
        new("--stdio-args")
        {
            Description = description + " Provide one quoted command-line string, for example --stdio-args=\"--serve -T --mode plan\".",
            Arity = ArgumentArity.ZeroOrOne
        };

    private static List<string> ParseStdioArguments(
        ParseResult parseResult,
        Option<string?> option,
        IReadOnlyList<string>? rawArgs)
    {
        if (parseResult.GetResult(option) is null)
        {
            return new List<string>();
        }

        var value = parseResult.GetValue(option);
        var hasAttachedEmptyValue = rawArgs?.Any(
            argument => argument.StartsWith("--stdio-args=", StringComparison.Ordinal)) == true;
        if (value is null && !hasAttachedEmptyValue)
        {
            throw new StdioCommandLineParseException(
                "a value is required; use --stdio-args= to provide an explicit empty value");
        }

        return StdioCommandLine.ParseArgumentsText(value).ToList();
    }

    private static Option<string?> CreateTransportOption()
    {
        var option = new Option<string?>("--transport")
        {
            Description = "Transport type: websocket (default), streamable_http, stdio.",
            DefaultValueFactory = _ => "websocket"
        };

        option.AcceptOnlyFromAmong("websocket", "streamable_http", "stdio");
        return option;
    }

    private static Option<string?> CreateOptionalTransportOption()
    {
        var option = new Option<string?>("--transport")
        {
            Description = "New transport type: websocket, streamable_http, stdio."
        };

        option.AcceptOnlyFromAmong("websocket", "streamable_http", "stdio");
        return option;
    }

    private static TransportType ParseTransport(string value) => value switch
    {
        "websocket" => TransportType.WebSocket,
        "streamable_http" => TransportType.StreamableHttp,
        "stdio" => TransportType.Stdio,
        _ => throw new ArgumentException($"Unsupported transport value '{value}'.", nameof(value))
    };

    private static TransportType? ParseOptionalTransport(string? value) =>
        value is null ? null : ParseTransport(value);

    private static Option<int?> CreateTimeoutOption() =>
        new Option<int?>("--timeout")
        {
            Description = $"Connection timeout in seconds ({AcpConnectionTimeoutPolicy.MinimumSeconds}–{AcpConnectionTimeoutPolicy.MaximumSeconds})."
        };
}
