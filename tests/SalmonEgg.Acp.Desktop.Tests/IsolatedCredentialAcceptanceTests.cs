using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using SalmonEgg.Acp.Client;
using SalmonEgg.Acp.Content;
using SalmonEgg.Acp.JsonRpc;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using SalmonEgg.Infrastructure.Client;
using SalmonEgg.Infrastructure.Services;
using SalmonEgg.Infrastructure.Storage;
using SalmonEgg.Infrastructure.Transport;
using Serilog;
using Xunit;

namespace SalmonEgg.Acp.Desktop.Tests;

public sealed class IsolatedCredentialAcceptanceTests
{
    [Fact]
    public async Task PiAgent_WithoutAmbientLogin_RequiresAndConsumesTheBoundSecret()
    {
        // Arrange: an isolated Pi directory contains only non-secret provider configuration.
        var secret = Environment.GetEnvironmentVariable("SALMONEGG_REAL_AGENT_SECRET");
        var agentDirectory = Environment.GetEnvironmentVariable("SALMONEGG_ISOLATED_PI_DIR");
        var command = Environment.GetEnvironmentVariable("SALMONEGG_REAL_AGENT_COMMAND");
        Assert.SkipWhen(!OperatingSystem.IsLinux() || string.IsNullOrWhiteSpace(secret)
            || string.IsNullOrWhiteSpace(agentDirectory) || string.IsNullOrWhiteSpace(command),
            "Run the isolated Pi/Secret Service acceptance wrapper on Linux.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(120));
        var token = timeout.Token;
        var directory = Path.Combine(Path.GetTempPath(), "salmon-egg-isolated-credential-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var storage = new LinuxSecretServiceSecureStorage();
        var paths = new ProbePaths(directory);
        var manager = new ConfigurationManager(storage, new FileSystemAppFileStore(), paths, NullLogger<ConfigurationManager>.Instance);
        var factory = new TransportFactory(Log.Logger, new TransportSupportPolicy(new PlatformCapabilityService()), new DesktopStdioTransportFactory());
        var profile = new ServerConfiguration
        {
            Id = "isolated-credential-" + Guid.NewGuid().ToString("N"),
            Name = "Isolated real Pi acceptance",
            Transport = TransportType.Stdio,
            StdioCommand = command!,
            StdioEnvironment = new(StringComparer.Ordinal) { ["PI_CODING_AGENT_DIR"] = agentDirectory! }
        };
        try
        {
            using (var unauthenticatedTransport = factory.CreateTransport(profile))
            using (var unauthenticated = new AcpClient(new DomainAcpTransportAdapter(unauthenticatedTransport)))
            {
                await unauthenticated.InitializeAsync(Initialize(), token);
                var failure = await Assert.ThrowsAsync<AcpException>(
                    () => unauthenticated.CreateSessionAsync(new SessionNewParams(directory, []), token));
                Assert.Equal(JsonRpcErrorCode.AuthenticationRequired, failure.ErrorCode);
                Assert.True(await unauthenticated.DisconnectAsync());
            }

            // Act: use the production OS keyring and reload through a new persistence owner.
            profile.Authentication = new AuthenticationConfig { ApiKey = secret };
            profile.CredentialBinding = CredentialBindingPolicy.Create(profile, CredentialSource.ApiKey,
                CredentialTarget.Environment, "SALMONEGG_PI_ACCEPTANCE_KEY");
            await manager.SaveConfigurationAsync(profile);
            manager = new ConfigurationManager(new LinuxSecretServiceSecureStorage(), new FileSystemAppFileStore(), paths,
                NullLogger<ConfigurationManager>.Instance);
            var reloaded = await manager.LoadConfigurationAsync(profile.Id);
            Assert.NotNull(reloaded);
            Assert.True(reloaded.Authentication?.ApiKey == secret, "The native keyring must restore the exact credential.");
            using (var transport = factory.CreateTransport(reloaded))
            using (var client = new AcpClient(new DomainAcpTransportAdapter(transport)))
            {
                var text = new StringBuilder();
                client.SessionUpdateReceived += (_, update) =>
                {
                    if (update.Update is AgentMessageUpdate { Content: TextContentBlock content })
                        lock (text) text.Append(content.Text);
                };
                await client.InitializeAsync(Initialize(), token);
                var session = await client.CreateSessionAsync(new SessionNewParams(directory, []), token);
                var marker = "ISOLATED_CREDENTIAL_" + Guid.NewGuid().ToString("N");
                var response = await client.SendPromptAsync(new SessionPromptParams(session.SessionId,
                    [new TextContentBlock("Do not use tools, inspect files or modify anything. Reply with exactly: " + marker)]), token);
                Assert.Equal(StopReason.EndTurn, response.StopReason);
                lock (text) Assert.True(text.ToString().Contains(marker, StringComparison.Ordinal), "The real Agent must answer this request.");
                Assert.True(await client.DisconnectAsync());
            }

            // Assert: clearing survives reload and cannot reuse an ambient credential.
            reloaded.Authentication = null;
            await manager.SaveConfigurationAsync(reloaded);
            var cleared = await manager.LoadConfigurationAsync(profile.Id);
            Assert.NotNull(cleared);
            Assert.Throws<InvalidOperationException>(() => factory.CreateTransport(cleared));
            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                Assert.False((await File.ReadAllTextAsync(file, token)).Contains(secret!, StringComparison.Ordinal),
                    "The credential must not enter plaintext product files.");
            }
        }
        finally
        {
            var saved = await manager.LoadConfigurationAsync(profile.Id);
            if (saved is not null)
            {
                saved.Authentication = null;
                await manager.SaveConfigurationAsync(saved);
            }
            Directory.Delete(directory, recursive: true);
        }
    }

    private static InitializeParams Initialize()
        => new(new ClientInfo("salmon-egg-isolated-credential-acceptance", "1"), new ClientCapabilities());

    private sealed class ProbePaths(string root) : IAppDataService
    {
        public string AppDataRootPath => root;
        public string ConfigRootPath => Path.Combine(root, "config");
        public string LogsDirectoryPath => Path.Combine(root, "logs");
        public string CacheRootPath => Path.Combine(root, "cache");
        public string ExportsDirectoryPath => Path.Combine(root, "exports");
    }
}
