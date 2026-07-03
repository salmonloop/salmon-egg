using System;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using SalmonEgg.Infrastructure.Services;

namespace SalmonEgg.Infrastructure.Storage;

public sealed class LinuxSecretServiceSecureStorage : ISecureStorage
{
    private const string SecretToolCommand = "secret-tool";
    private const string ServiceAttributeName = "service";
    private const string ServiceAttributeValue = "SalmonEgg";
    private const string KeyAttributeName = "key";
    private const string Label = "SalmonEgg";
    private readonly ISecretToolRunner _runner;

    public LinuxSecretServiceSecureStorage()
        : this(new SecretToolRunner())
    {
    }

    internal LinuxSecretServiceSecureStorage(ISecretToolRunner runner)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    public async Task SaveAsync(string key, string value)
    {
        ValidateKey(key);
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        var result = await _runner.RunAsync(
            [
                "store",
                "--label",
                Label,
                ServiceAttributeName,
                ServiceAttributeValue,
                KeyAttributeName,
                GetKeyHash(key)
            ],
            value).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(CreateUnavailableMessage(result));
        }
    }

    public async Task<string?> LoadAsync(string key)
    {
        ValidateKey(key);
        var result = await _runner.RunAsync(
            [
                "lookup",
                ServiceAttributeName,
                ServiceAttributeValue,
                KeyAttributeName,
                GetKeyHash(key)
            ],
            standardInput: null).ConfigureAwait(false);

        return result.ExitCode == 0 ? TrimSecretToolTerminator(result.StandardOutput) : null;
    }

    public async Task DeleteAsync(string key)
    {
        ValidateKey(key);
        await _runner.RunAsync(
            [
                "clear",
                ServiceAttributeName,
                ServiceAttributeValue,
                KeyAttributeName,
                GetKeyHash(key)
            ],
            standardInput: null).ConfigureAwait(false);
    }

    private static string CreateUnavailableMessage(SecretToolResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.StandardError))
        {
            return $"Linux Secret Service is unavailable: {result.StandardError.Trim()}";
        }

        return "Linux Secret Service is unavailable. Install libsecret-tools and ensure a Secret Service provider is running.";
    }

    private static string GetKeyHash(string key)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(key));
        return Convert.ToBase64String(hash)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string TrimSecretToolTerminator(string value)
    {
        if (value.EndsWith("\r\n", StringComparison.Ordinal))
        {
            return value.Substring(0, value.Length - 2);
        }

        return value.EndsWith("\n", StringComparison.Ordinal)
            ? value.Substring(0, value.Length - 1)
            : value;
    }

    private static void ValidateKey(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            throw new ArgumentNullException(nameof(key));
        }
    }

    internal interface ISecretToolRunner
    {
        Task<SecretToolResult> RunAsync(string[] arguments, string? standardInput);
    }

    internal sealed class SecretToolRunner : ISecretToolRunner
    {
        public async Task<SecretToolResult> RunAsync(string[] arguments, string? standardInput)
        {
            if (!RuntimeCommandResolver.TryResolve(SecretToolCommand, out var secretToolPath))
            {
                return new SecretToolResult(127, string.Empty, "secret-tool was not found in PATH.");
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = secretToolPath,
                RedirectStandardInput = standardInput != null,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                return new SecretToolResult(1, string.Empty, "secret-tool could not be started.");
            }

            if (standardInput != null)
            {
                await process.StandardInput.WriteAsync(standardInput).ConfigureAwait(false);
                process.StandardInput.Close();
            }

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            await Task.Run(process.WaitForExit).ConfigureAwait(false);

            return new SecretToolResult(
                process.ExitCode,
                await outputTask.ConfigureAwait(false),
                await errorTask.ConfigureAwait(false));
        }
    }

    internal sealed class SecretToolResult
    {
        public SecretToolResult(int exitCode, string standardOutput, string standardError)
        {
            ExitCode = exitCode;
            StandardOutput = standardOutput ?? string.Empty;
            StandardError = standardError ?? string.Empty;
        }

        public int ExitCode { get; }

        public string StandardOutput { get; }

        public string StandardError { get; }
    }
}
