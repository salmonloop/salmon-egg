using System;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Cli.Output;
using SalmonEgg.Cli.Hosting;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Cli.Commands.Credentials;

public sealed class CredentialsHandler
{
    private readonly ICliOutput _output;
    private readonly IConfigurationService _configurationService;
    private readonly IServerCredentialService _credentialService;
    private readonly ICliInput _input;

    public CredentialsHandler(
        ICliOutput output,
        IConfigurationService configurationService,
        IServerCredentialService credentialService,
        ICliInput input)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        _credentialService = credentialService ?? throw new ArgumentNullException(nameof(credentialService));
        _input = input ?? throw new ArgumentNullException(nameof(input));
    }

    public CredentialsHandler(
        ICliOutput output,
        IConfigurationService configurationService,
        IServerCredentialService credentialService)
        : this(output, configurationService, credentialService, new TextCliInput(Console.In))
    {
    }

    public async Task<int> SetFromStdinAsync(
        string serverId,
        bool tokenFromStdin,
        bool apiKeyFromStdin,
        CancellationToken cancellationToken)
    {
        if (tokenFromStdin == apiKeyFromStdin)
        {
            await _output.WriteErrorAsync("Specify exactly one of --token-stdin or --api-key-stdin.").ConfigureAwait(false);
            return CliExitCodes.Usage;
        }

        var value = await _input.ReadSecretLineAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(value))
        {
            await _output.WriteErrorAsync("Credential value cannot be empty.").ConfigureAwait(false);
            return CliExitCodes.Usage;
        }

        return await SetAsync(
            serverId,
            tokenFromStdin ? value : null,
            apiKeyFromStdin ? value : null,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> SetAsync(
        string serverId,
        string? token,
        string? apiKey,
        CancellationToken cancellationToken)
    {
        if ((token is null) == (apiKey is null))
        {
            await _output.WriteErrorAsync("Specify exactly one credential input.").ConfigureAwait(false);
            return CliExitCodes.Usage;
        }

        var credential = token ?? apiKey;
        if (string.IsNullOrWhiteSpace(credential))
        {
            await _output.WriteErrorAsync("Credential value cannot be empty.").ConfigureAwait(false);
            return CliExitCodes.Usage;
        }

        ServerConfiguration config;
        try
        {
            var loaded = await LoadConfigurationAsync(serverId, cancellationToken).ConfigureAwait(false);
            if (loaded is null)
            {
                return CliExitCodes.Failure;
            }

            config = loaded;
            config.Authentication = token is not null
                ? new AuthenticationConfig { Token = token }
                : new AuthenticationConfig { ApiKey = apiKey };

            await _configurationService.SaveConfigurationAsync(config).ConfigureAwait(false);
        }
        catch (ConfigurationPersistenceException ex)
        {
            await _output.WriteErrorAsync($"Credential save failed: {ex.UserMessage}").ConfigureAwait(false);
            return CliExitCodes.Failure;
        }

        await _output.WriteAsync(
            token is not null
                ? $"Token saved for server '{serverId}'."
                : $"API key saved for server '{serverId}'.").ConfigureAwait(false);
        return CliExitCodes.Success;
    }

    public async Task<int> ClearAsync(string serverId, CancellationToken cancellationToken)
    {
        try
        {
            var loaded = await LoadConfigurationAsync(serverId, cancellationToken).ConfigureAwait(false);
            if (loaded is null)
            {
                return CliExitCodes.Failure;
            }

            loaded.Authentication = null;
            await _configurationService.SaveConfigurationAsync(loaded).ConfigureAwait(false);
        }
        catch (ConfigurationPersistenceException ex)
        {
            await _output.WriteErrorAsync($"Credential clear failed: {ex.UserMessage}").ConfigureAwait(false);
            return CliExitCodes.Failure;
        }

        await _output.WriteAsync($"Credentials cleared for server '{serverId}'.").ConfigureAwait(false);
        return CliExitCodes.Success;
    }

    public async Task<int> HasAsync(string serverId, CancellationToken cancellationToken)
    {
        try
        {
            if (await LoadConfigurationAsync(serverId, cancellationToken).ConfigureAwait(false) is null)
            {
                return CliExitCodes.Failure;
            }

            var status = await _credentialService.GetStatusAsync(serverId).ConfigureAwait(false);
            await _output.WriteAsync($"token: {(status.HasToken ? "present" : "absent")}").ConfigureAwait(false);
            await _output.WriteAsync($"api_key: {(status.HasApiKey ? "present" : "absent")}").ConfigureAwait(false);
            return CliExitCodes.Success;
        }
        catch (ConfigurationPersistenceException ex)
        {
            await _output.WriteErrorAsync($"Credential status failed: {ex.UserMessage}").ConfigureAwait(false);
            return CliExitCodes.Failure;
        }
    }

    private async Task<ServerConfiguration?> LoadConfigurationAsync(
        string serverId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(serverId))
        {
            await _output.WriteErrorAsync("Server ID cannot be empty.").ConfigureAwait(false);
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var config = await _configurationService.LoadConfigurationAsync(serverId).ConfigureAwait(false);
        if (config is not null)
        {
            return config;
        }

        await _output.WriteErrorAsync($"Server '{serverId}' not found.").ConfigureAwait(false);
        return null;
    }
}
