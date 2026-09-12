using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using SalmonEgg.Acp.Client;
using SalmonEgg.Acp.Content;
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

/// <summary>Verifies explicit credential wiring; the Agent's existing login can also remain available.</summary>
public sealed class RealCredentialAcceptanceTests
{
    [Fact]
    public async Task RealAgent_BoundCredential_SurvivesProfileReloadAndClearPreventsReconnect()
    {
        // Arrange
        var secret = Environment.GetEnvironmentVariable("SALMONEGG_REAL_AGENT_SECRET");
        var command = Environment.GetEnvironmentVariable("SALMONEGG_REAL_AGENT_COMMAND");
        var name = Environment.GetEnvironmentVariable("SALMONEGG_REAL_AGENT_SECRET_ENV");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(command) || string.IsNullOrWhiteSpace(name),
            "Supply an installed ACP Agent and an explicit per-process credential binding.");
        var directory = Path.Combine(Path.GetTempPath(), "salmon-egg-credential-acceptance-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(90));
        var token = timeout.Token;
        try
        {
            var profile = new ServerConfiguration
            {
                Id = "acceptance",
                Name = "Real Agent acceptance",
                Transport = TransportType.Stdio,
                StdioCommand = command!,
                Authentication = new AuthenticationConfig { ApiKey = secret },
            };
            profile.CredentialBinding = CredentialBindingPolicy.Create(profile, CredentialSource.ApiKey, CredentialTarget.Environment, name!);
            var storage = new VolatileSecureStorage();
            var paths = new ProbePaths(directory);
            var manager = new ConfigurationManager(storage, new FileSystemAppFileStore(), paths, NullLogger<ConfigurationManager>.Instance);
            await manager.SaveConfigurationAsync(profile);
            var reloaded = await manager.LoadConfigurationAsync(profile.Id);
            Assert.NotNull(reloaded);
            var yaml = await File.ReadAllTextAsync(Path.Combine(paths.ConfigRootPath, "servers", "acceptance.yaml"), token);
            Assert.False(yaml.Contains(secret!, StringComparison.Ordinal), "Profile persistence must never expose the real credential.");
            var factory = new TransportFactory(Log.Logger, new TransportSupportPolicy(new PlatformCapabilityService()), new DesktopStdioTransportFactory());
            using var transport = factory.CreateTransport(reloaded);
            using var client = new AcpClient(new DomainAcpTransportAdapter(transport));
            var text = new StringBuilder();
            client.SessionUpdateReceived += (_, update) =>
            {
                if (update.Update is AgentMessageUpdate { Content: TextContentBlock content })
                    lock (text) text.Append(content.Text);
            };

            // Act: the configured real Agent uses its normal credential environment, never an ACP metadata secret.
            await client.InitializeAsync(new InitializeParams(new ClientInfo("salmon-egg-credential-interop", "1"), new ClientCapabilities()), token);
            var invocation = Assert.IsAssignableFrom<IStdioInvocationSource>(transport).StdioInvocation;
            Assert.NotNull(invocation);
            Assert.True(invocation.Environment.TryGetValue(name!, out var injected) && injected == secret,
                "The effective launch snapshot must contain the explicit binding.");
            var session = await client.CreateSessionAsync(new SessionNewParams(directory, []), token);
            var marker = "CREDENTIAL_BOUND_" + Guid.NewGuid().ToString("N");
            await client.SendPromptAsync(new SessionPromptParams(session.SessionId,
                [new TextContentBlock("Do not use tools or access files. Reply with exactly: " + marker)]), token);
            lock (text) Assert.Contains(marker, text.ToString(), StringComparison.Ordinal);
            Assert.True(await client.DisconnectAsync());
            reloaded.Authentication = null;
            await manager.SaveConfigurationAsync(reloaded);
            var cleared = await manager.LoadConfigurationAsync(profile.Id);

            // Assert: clearing retains the explicit binding, and cannot fall back to an inherited secret.
            Assert.NotNull(cleared);
            var resolution = CredentialBindingResolver.Resolve(cleared);
            Assert.False(resolution.IsSuccess);
            Assert.Equal(CredentialBindingValidationError.MissingCredential, resolution.ErrorKind);
            Assert.Throws<InvalidOperationException>(() => factory.CreateTransport(cleared));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class ProbePaths(string root) : IAppDataService
    {
        public string AppDataRootPath => root;
        public string ConfigRootPath => Path.Combine(root, "config");
        public string LogsDirectoryPath => Path.Combine(root, "logs");
        public string CacheRootPath => Path.Combine(root, "cache");
        public string ExportsDirectoryPath => Path.Combine(root, "exports");
    }
}
