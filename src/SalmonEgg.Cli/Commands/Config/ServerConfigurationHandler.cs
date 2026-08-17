using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using SalmonEgg.Cli.Output;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Cli.Commands.Config;

public sealed record ServerConfigurationPatch(
    string? Token,
    string? ApiKey,
    bool AuthenticationSpecified,
    string? AuthenticationMode,
    bool ProxySpecified,
    ProxyMode? ProxyMode,
    bool ProxyUrlSpecified,
    string? ProxyUrl);

/// <remarks>
/// Handler methods are pure business logic: they call domain interfaces and write to
/// <see cref="ICliOutput"/>. They never touch YAML paths, secure-storage keys, or any
/// Infrastructure concrete type.
/// </remarks>
public sealed class ServerConfigurationHandler
{
    private static readonly ServerConfigurationPatch EmptyPatch = new(
        null, null, false, null, false, null, false, null);

    private readonly IConfigurationService _configurations;
    private readonly IValidator<ServerConfiguration> _validator;
    private readonly ICliOutput _output;

    public ServerConfigurationHandler(
        IConfigurationService configurations,
        IValidator<ServerConfiguration> validator,
        ICliOutput output)
    {
        _configurations = configurations ?? throw new ArgumentNullException(nameof(configurations));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    // ── list ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Lists all server configurations (id, name, transport). One line per entry.
    /// </summary>
    public async Task<int> ListAsync(CancellationToken cancellationToken = default)
    {
        var configs = (await _configurations.ListConfigurationsAsync().ConfigureAwait(false)).ToList();

        if (configs.Count == 0)
        {
            await _output.WriteAsync("(no servers configured)").ConfigureAwait(false);
            return CliExitCodes.Success;
        }

        foreach (var config in configs)
        {
            await _output.WriteAsync(FormatListLine(config)).ConfigureAwait(false);
        }

        return CliExitCodes.Success;
    }

    // ── show ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Shows non-sensitive fields of a single server configuration.
    /// </summary>
    public async Task<int> ShowAsync(string id, CancellationToken cancellationToken = default)
    {
        ServerConfiguration config;
        try
        {
            var loaded = await _configurations.LoadConfigurationAsync(id).ConfigureAwait(false);
            if (loaded is null)
            {
                await _output.WriteErrorAsync($"Server '{id}' not found.").ConfigureAwait(false);
                return CliExitCodes.Usage;
            }

            config = loaded;
        }
        catch (ConfigurationPersistenceException ex)
        {
            await _output.WriteErrorAsync(ex.UserMessage).ConfigureAwait(false);
            return CliExitCodes.Failure;
        }

        foreach (var line in FormatShowLines(config))
        {
            await _output.WriteAsync(line).ConfigureAwait(false);
        }

        return CliExitCodes.Success;
    }

    // ── add ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a new server configuration. The ID is generated internally.
    /// </summary>
    public Task<int> AddAsync(
        string name,
        string? url,
        TransportType transport,
        string? stdioCommand,
        List<string>? stdioArgs,
        int? timeout,
        CancellationToken cancellationToken = default)
        => AddAsync(name, url, transport, stdioCommand, stdioArgs, timeout, EmptyPatch, cancellationToken);

    public async Task<int> AddAsync(
        string name,
        string? url,
        TransportType transport,
        string? stdioCommand,
        List<string>? stdioArgs,
        int? timeout,
        ServerConfigurationPatch patch,
        CancellationToken cancellationToken = default)
    {
        var config = new ServerConfiguration
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            ServerUrl = url ?? string.Empty,
            Transport = transport,
            StdioCommand = stdioCommand ?? string.Empty,
            StdioArguments = stdioArgs ?? new List<string>(),
            ConnectionTimeout = timeout ?? AcpConnectionTimeoutPolicy.DefaultSeconds,
            Proxy = new ProxyConfig { Mode = ProxyConfig.DefaultMode }
        };

        if (!TryApplyPatch(config, patch, out var patchError, isAdd: true))
        {
            await _output.WriteErrorAsync(patchError!).ConfigureAwait(false);
            return CliExitCodes.Usage;
        }

        var validation = await _validator.ValidateAsync(config, cancellationToken).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            foreach (var error in validation.Errors)
            {
                await _output.WriteErrorAsync($"Validation: {error.ErrorMessage}").ConfigureAwait(false);
            }

            return CliExitCodes.Usage;
        }

        try
        {
            await _configurations.SaveConfigurationAsync(config).ConfigureAwait(false);
        }
        catch (ConfigurationPersistenceException ex)
        {
            await _output.WriteErrorAsync($"Save failed: {ex.UserMessage}").ConfigureAwait(false);
            return CliExitCodes.Failure;
        }

        await _output.WriteAsync($"Server added: {config.Id}").ConfigureAwait(false);
        return CliExitCodes.Success;
    }

    // ── update ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Updates specified fields of an existing server configuration, preserving all others.
    /// </summary>
    public Task<int> UpdateAsync(
        string id,
        string? name,
        string? url,
        TransportType? transport,
        string? stdioCommand,
        List<string>? stdioArgs,
        int? timeout,
        CancellationToken cancellationToken = default)
        => UpdateAsync(id, name, url, transport, stdioCommand, stdioArgs, timeout, EmptyPatch, cancellationToken);

    public async Task<int> UpdateAsync(
        string id,
        string? name,
        string? url,
        TransportType? transport,
        string? stdioCommand,
        List<string>? stdioArgs,
        int? timeout,
        ServerConfigurationPatch patch,
        CancellationToken cancellationToken = default)
    {
        ServerConfiguration config;
        try
        {
            var loaded = await _configurations.LoadConfigurationAsync(id).ConfigureAwait(false);
            if (loaded is null)
            {
                await _output.WriteErrorAsync($"Server '{id}' not found.").ConfigureAwait(false);
                return CliExitCodes.Usage;
            }

            config = loaded;
        }
        catch (ConfigurationPersistenceException ex)
        {
            await _output.WriteErrorAsync(ex.UserMessage).ConfigureAwait(false);
            return CliExitCodes.Failure;
        }

        // Merge: only override fields that were explicitly provided.
        if (name is not null) config.Name = name;
        if (url is not null) config.ServerUrl = url;
        if (transport is not null) config.Transport = transport.Value;
        if (stdioCommand is not null) config.StdioCommand = stdioCommand;
        if (stdioArgs is not null) config.StdioArguments = stdioArgs;
        if (timeout is not null) config.ConnectionTimeout = timeout.Value;

        if (!TryApplyPatch(config, patch, out var patchError, isAdd: false))
        {
            await _output.WriteErrorAsync(patchError!).ConfigureAwait(false);
            return CliExitCodes.Usage;
        }

        var validation = await _validator.ValidateAsync(config, cancellationToken).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            foreach (var error in validation.Errors)
            {
                await _output.WriteErrorAsync($"Validation: {error.ErrorMessage}").ConfigureAwait(false);
            }

            return CliExitCodes.Usage;
        }

        try
        {
            await _configurations.SaveConfigurationAsync(config).ConfigureAwait(false);
        }
        catch (ConfigurationPersistenceException ex)
        {
            await _output.WriteErrorAsync($"Save failed: {ex.UserMessage}").ConfigureAwait(false);
            return CliExitCodes.Failure;
        }

        await _output.WriteAsync($"Server updated: {id}").ConfigureAwait(false);
        return CliExitCodes.Success;
    }

    // ── remove ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Removes a server configuration and its associated credentials.
    /// Requires explicit confirmation via <paramref name="confirmed"/>.
    /// </summary>
    public async Task<int> RemoveAsync(string id, bool confirmed, CancellationToken cancellationToken = default)
    {
        if (!confirmed)
        {
            await _output.WriteErrorAsync(
                $"Add --yes to confirm removal of server '{id}'.").ConfigureAwait(false);
            return CliExitCodes.Usage;
        }

        try
        {
            // Load first both to confirm existence and to surface a transient I/O failure
            // (locked file, permission) as a retryable Remove-failed error rather than a
            // misleading "not found" that would contradict a later successful delete.
            var loaded = await _configurations.LoadConfigurationAsync(id).ConfigureAwait(false);
            if (loaded is null)
            {
                await _output.WriteErrorAsync($"Server '{id}' not found.").ConfigureAwait(false);
                return CliExitCodes.Usage;
            }

            await _configurations.DeleteConfigurationAsync(id).ConfigureAwait(false);
        }
        catch (ConfigurationPersistenceException ex)
        {
            await _output.WriteErrorAsync($"Remove failed: {ex.UserMessage}").ConfigureAwait(false);
            return CliExitCodes.Failure;
        }

        await _output.WriteAsync($"Server removed: {id}").ConfigureAwait(false);
        return CliExitCodes.Success;
    }

    private static bool TryApplyPatch(
        ServerConfiguration config,
        ServerConfigurationPatch patch,
        out string? error,
        bool isAdd)
    {
        error = null;

        var hasToken = patch.Token is not null;
        var hasApiKey = patch.ApiKey is not null;
        if (hasToken && hasApiKey)
        {
            error = "Specify at most one of --token or --api-key.";
            return false;
        }

        if (hasToken && string.IsNullOrWhiteSpace(patch.Token)
            || hasApiKey && string.IsNullOrWhiteSpace(patch.ApiKey))
        {
            error = "Credential value cannot be empty.";
            return false;
        }

        if (patch.AuthenticationSpecified)
        {
            var mode = patch.AuthenticationMode?.Trim().ToLowerInvariant();
            if (mode is not (null or "none" or "bearer_token" or "api_key"))
            {
                error = "Authentication mode must be none, bearer_token, or api_key.";
                return false;
            }

            if (mode == "none")
            {
                if (hasToken || hasApiKey)
                {
                    error = "--auth none cannot be combined with --token or --api-key.";
                    return false;
                }

                config.Authentication = null;
            }
            else if (mode == "bearer_token")
            {
                if (hasApiKey)
                {
                    error = "--auth bearer_token requires --token, not --api-key.";
                    return false;
                }

                var token = patch.Token ?? (!isAdd ? config.Authentication?.Token : null);
                if (string.IsNullOrWhiteSpace(token))
                {
                    error = "Bearer-token authentication requires --token on a new configuration.";
                    return false;
                }

                config.Authentication = new AuthenticationConfig { Token = token };
            }
            else if (mode == "api_key")
            {
                if (hasToken)
                {
                    error = "--auth api_key requires --api-key, not --token.";
                    return false;
                }

                var apiKey = patch.ApiKey ?? (!isAdd ? config.Authentication?.ApiKey : null);
                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    error = "API-key authentication requires --api-key on a new configuration.";
                    return false;
                }

                config.Authentication = new AuthenticationConfig { ApiKey = apiKey };
            }
            else if (hasToken || hasApiKey)
            {
                config.Authentication = hasToken
                    ? new AuthenticationConfig { Token = patch.Token }
                    : new AuthenticationConfig { ApiKey = patch.ApiKey };
            }
        }
        else if (hasToken || hasApiKey)
        {
            config.Authentication = hasToken
                ? new AuthenticationConfig { Token = patch.Token }
                : new AuthenticationConfig { ApiKey = patch.ApiKey };
        }

        if (patch.ProxyUrlSpecified && (!patch.ProxySpecified || patch.ProxyMode != ProxyMode.Custom))
        {
            error = "--proxy-url requires --proxy-mode custom.";
            return false;
        }

        if (patch.ProxySpecified)
        {
            var mode = patch.ProxyMode ?? ProxyConfig.DefaultMode;
            config.Proxy = new ProxyConfig
            {
                Mode = mode,
                ProxyUrl = mode == ProxyMode.Custom ? patch.ProxyUrl : null
            };
        }

        return true;
    }


    private static string FormatListLine(ServerConfiguration config)
        => $"{config.Id}  {config.Name}  {TransportLabel(config.Transport)}";

    internal static IEnumerable<string> FormatShowLines(ServerConfiguration config)
    {
        yield return $"id:         {config.Id}";
        yield return $"name:       {config.Name}";
        yield return $"transport:  {TransportLabel(config.Transport)}";

        if (config.Transport == TransportType.Stdio)
        {
            yield return $"command:    {config.StdioCommand}";
            if (config.StdioArguments.Count > 0)
            {
                // Reuse the domain formatter so arguments containing spaces stay round-trippable
                // instead of looking like several separate arguments.
                yield return $"args:       {StdioCommandLine.FormatArgumentsText(config.StdioArguments)}";
            }
        }
        else
        {
            yield return $"url:        {config.ServerUrl}";
        }

        yield return $"timeout:    {config.ConnectionTimeout}s";

        var proxyMode = config.Proxy?.Mode ?? ProxyConfig.DefaultMode;
        yield return $"proxy:      {ProxyModeLabel(proxyMode)}";
        if (proxyMode == ProxyMode.Custom && !string.IsNullOrWhiteSpace(config.Proxy?.ProxyUrl))
        {
            yield return $"proxy_url:  {config.Proxy.ProxyUrl}";
        }

        if (!string.IsNullOrEmpty(config.Authentication?.Token))
        {
            yield return "auth:       bearer_token (credential stored separately)";
        }
        else if (!string.IsNullOrEmpty(config.Authentication?.ApiKey))
        {
            yield return "auth:       api_key (credential stored separately)";
        }
        else if (config.Authentication is not null)
        {
            yield return "auth:       configured (credential unavailable)";
        }
    }

    private static string TransportLabel(TransportType t) => t switch
    {
        TransportType.Stdio => "stdio",
        TransportType.StreamableHttp => "streamable_http",
        _ => "websocket"
    };

    private static string ProxyModeLabel(ProxyMode mode) => mode switch
    {
        ProxyMode.None => "none",
        ProxyMode.Custom => "custom",
        _ => "system"
    };
}
