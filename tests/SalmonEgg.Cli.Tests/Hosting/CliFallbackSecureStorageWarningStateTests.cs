using System.IO;
using SalmonEgg.Cli.Hosting;
using SalmonEgg.Cli.Output;
using SalmonEgg.Infrastructure.Storage;

namespace SalmonEgg.Cli.Tests.Hosting;

public sealed class CliFallbackSecureStorageWarningStateTests
{
    [Fact]
    public async Task FallbackSecretUse_WritesOneNonSecretWarningToStderr()
    {
        var warningState = new CliFallbackSecureStorageWarningState();
        var storage = new FallbackSecureStorage(
            new UnavailableSecureStorage(),
            new VolatileSecureStorage(),
            warningState);
        await storage.SaveAsync("credential-key", "credential-value");
        await using var stdout = new StringWriter();
        await using var stderr = new StringWriter();
        var output = new TextCliOutput(stdout, stderr);

        await warningState.WriteIfNeededAsync(output);
        await warningState.WriteIfNeededAsync(output);

        Assert.Contains("plaintext fallback storage", stderr.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("credential-key", stderr.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("credential-value", stderr.ToString(), StringComparison.Ordinal);
        Assert.Single(stderr.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
        Assert.Empty(stdout.ToString());
    }

    private sealed class UnavailableSecureStorage : ISecureStorage
    {
        public Task SaveAsync(string key, string value)
            => throw new SecureStorageUnavailableException("unavailable");

        public Task<string?> LoadAsync(string key)
            => throw new SecureStorageUnavailableException("unavailable");

        public Task DeleteAsync(string key)
            => throw new SecureStorageUnavailableException("unavailable");
    }
}
