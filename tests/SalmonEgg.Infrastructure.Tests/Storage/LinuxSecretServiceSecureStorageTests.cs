using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SalmonEgg.Infrastructure.Storage;

namespace SalmonEgg.Infrastructure.Tests.Storage;

public sealed class LinuxSecretServiceSecureStorageTests
{
    [Fact]
    public async Task SaveAsync_StoresSecretViaStandardInput()
    {
        var runner = new RecordingSecretToolRunner(new LinuxSecretServiceSecureStorage.SecretToolResult(0, string.Empty, string.Empty));
        var storage = new LinuxSecretServiceSecureStorage(runner);

        await storage.SaveAsync("salmonegg/config/profile/token", "secret-token");

        var call = Assert.Single(runner.Calls);
        Assert.Equal("secret-token", call.StandardInput);
        Assert.Contains("store", call.Arguments);
        Assert.DoesNotContain("secret-token", call.Arguments);
        Assert.Contains("--label", call.Arguments);
        Assert.Contains("SalmonEgg", call.Arguments);
    }

    [Fact]
    public async Task LoadAsync_WhenSecretExists_ReturnsSecretWithoutToolLineTerminator()
    {
        var runner = new RecordingSecretToolRunner(new LinuxSecretServiceSecureStorage.SecretToolResult(0, "secret-token\n", string.Empty));
        var storage = new LinuxSecretServiceSecureStorage(runner);

        var value = await storage.LoadAsync("salmonegg/config/profile/token");

        Assert.Equal("secret-token", value);
        var call = Assert.Single(runner.Calls);
        Assert.Contains("lookup", call.Arguments);
    }

    [Fact]
    public async Task LoadAsync_WhenSecretToolUnavailable_ReturnsNull()
    {
        var runner = new RecordingSecretToolRunner(new LinuxSecretServiceSecureStorage.SecretToolResult(127, string.Empty, "missing"));
        var storage = new LinuxSecretServiceSecureStorage(runner);

        var value = await storage.LoadAsync("salmonegg/config/profile/token");

        Assert.Null(value);
    }

    [Fact]
    public async Task SaveAsync_WhenSecretToolUnavailable_FailsClosed()
    {
        var runner = new RecordingSecretToolRunner(new LinuxSecretServiceSecureStorage.SecretToolResult(127, string.Empty, "missing"));
        var storage = new LinuxSecretServiceSecureStorage(runner);

        await Assert.ThrowsAsync<SecureStorageUnavailableException>(
            () => storage.SaveAsync("salmonegg/config/profile/token", "secret-token"));
    }

    private sealed class RecordingSecretToolRunner : LinuxSecretServiceSecureStorage.ISecretToolRunner
    {
        private readonly LinuxSecretServiceSecureStorage.SecretToolResult _result;

        public RecordingSecretToolRunner(LinuxSecretServiceSecureStorage.SecretToolResult result)
        {
            _result = result;
        }

        public List<Call> Calls { get; } = new();

        public Task<LinuxSecretServiceSecureStorage.SecretToolResult> RunAsync(
            string[] arguments,
            string? standardInput)
        {
            Calls.Add(new Call(arguments, standardInput));
            return Task.FromResult(_result);
        }
    }

    private sealed class Call
    {
        public Call(string[] arguments, string? standardInput)
        {
            Arguments = arguments;
            StandardInput = standardInput;
        }

        public string[] Arguments { get; }

        public string? StandardInput { get; }
    }
}
