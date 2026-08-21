using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using SalmonEgg.Application.Validators;
using SalmonEgg.Cli.Commands.Config;
using SalmonEgg.Cli.Output;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using SalmonEgg.Infrastructure.Storage;

namespace SalmonEgg.Cli.Tests.Commands.Config;

/// <summary>
/// Builds a handler over the real configuration stack in an isolated app-data root.
/// </summary>
/// <remarks>
/// The production <see cref="ConfigurationManager"/> is used rather than a mock so these tests
/// cover the actual YAML round-trip, schema handling, and credential-key linkage that the CLI
/// depends on. Only the secure store and console are substituted.
/// </remarks>
internal sealed class HandlerFixture : IDisposable
{
    private readonly string _root;

    public HandlerFixture()
    {
        _root = Path.Combine(Path.GetTempPath(), "SalmonEggCliTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        Environment.SetEnvironmentVariable("SALMONEGG_APPDATA_ROOT", _root, EnvironmentVariableTarget.Process);

        SecureStorage = new RecordingSecureStorage();
        Output = new RecordingCliOutput();
        Configurations = new ConfigurationManager(
            SecureStorage,
            new FileSystemAppFileStore(),
            new AppDataService(),
            NullLogger<ConfigurationManager>.Instance);

        Handler = new ServerConfigurationHandler(
            Configurations,
            new ServerConfigurationValidator(),
            Output);
    }

    public RecordingSecureStorage SecureStorage { get; }

    public string AppDataRoot => _root;

    public RecordingCliOutput Output { get; }

    public IConfigurationService Configurations { get; }

    public ServerConfigurationHandler Handler { get; }

    public async Task SeedAsync(string id, string name, string url, int? timeout = null, string? token = null)
    {
        var config = new ServerConfiguration
        {
            Id = id,
            Name = name,
            ServerUrl = url,
            Transport = TransportType.WebSocket,
            ConnectionTimeout = timeout ?? AcpConnectionTimeoutPolicy.DefaultSeconds
        };

        if (token is not null)
        {
            config.Authentication = new AuthenticationConfig { Token = token };
        }

        await Configurations.SaveConfigurationAsync(config);
        Output.Reset();
    }

    public async Task SeedStdioAsync(string id, string name, string command, List<string> arguments)
    {
        await Configurations.SaveConfigurationAsync(new ServerConfiguration
        {
            Id = id,
            Name = name,
            Transport = TransportType.Stdio,
            StdioCommand = command,
            StdioArguments = arguments
        });
        Output.Reset();
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("SALMONEGG_APPDATA_ROOT", null, EnvironmentVariableTarget.Process);
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup failures.
        }
    }
}

internal sealed class RecordingCliOutput : ICliOutput
{
    public List<string> Lines { get; } = new();

    public List<string> Errors { get; } = new();

    public Task WriteAsync(string message)
    {
        Lines.Add(message);
        return Task.CompletedTask;
    }

    public Task WriteErrorAsync(string message)
    {
        Errors.Add(message);
        return Task.CompletedTask;
    }

    public void Reset()
    {
        Lines.Clear();
        Errors.Clear();
    }
}

internal sealed class RecordingSecureStorage : ISecureStorage
{
    private readonly ConcurrentDictionary<string, string> _values = new(StringComparer.Ordinal);

    public IReadOnlyCollection<string> Keys => _values.Keys.ToArray();

    public Task SaveAsync(string key, string value)
    {
        _values[key] = value;
        return Task.CompletedTask;
    }

    public Task<string?> LoadAsync(string key)
    {
        _values.TryGetValue(key, out var value);
        return Task.FromResult(value);
    }

    public Task DeleteAsync(string key)
    {
        _values.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}
