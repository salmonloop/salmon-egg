using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SalmonEgg.Application.Validators;
using SalmonEgg.Cli.Commands.Config;
using SalmonEgg.Cli.Output;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Cli.Tests.Commands.Config;

public sealed class ServerConfigurationHandlerTests
{
    [Fact]
    public async Task AddAsync_WithAuthenticationAndCustomProxy_PersistsModeWithoutSecretInYaml()
    {
        using var fixture = new HandlerFixture();
        const string token = "cli-token-secret";
        var patch = new ServerConfigurationPatch(
            token, null, true, "bearer_token", true, ProxyMode.Custom, true, "http://proxy.example:8080");

        var exitCode = await fixture.Handler.AddAsync(
            "Configured Agent",
            "https://agent.example",
            TransportType.StreamableHttp,
            null,
            null,
            null,
            patch,
            TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Success, exitCode);
        var stored = Assert.Single(await fixture.Configurations.ListConfigurationsAsync());
        var reloaded = await fixture.Configurations.LoadConfigurationAsync(stored.Id);
        Assert.NotNull(reloaded);
        Assert.Equal(token, reloaded!.Authentication?.Token);
        Assert.Equal(ProxyMode.Custom, reloaded.Proxy?.Mode);
        Assert.Equal("http://proxy.example:8080", reloaded.Proxy?.ProxyUrl);

        var yamlPath = Path.Combine(fixture.AppDataRoot, "config", "servers", $"{stored.Id}.yaml");
        var yaml = await File.ReadAllTextAsync(yamlPath, TestContext.Current.CancellationToken);
        Assert.DoesNotContain(token, yaml, StringComparison.Ordinal);
        Assert.Contains("bearer_token", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateAsync_WithApiKey_ReplacesTokenAndPreservesProxyWhenOmitted()
    {
        using var fixture = new HandlerFixture();
        await fixture.SeedAsync("update-auth", "Agent", "https://agent.example", token: "old-token");
        var existing = await fixture.Configurations.LoadConfigurationAsync("update-auth");
        Assert.NotNull(existing);
        existing!.Proxy = new ProxyConfig { Mode = ProxyMode.Custom, ProxyUrl = "http://proxy.example" };
        await fixture.Configurations.SaveConfigurationAsync(existing);
        fixture.Output.Reset();

        var patch = new ServerConfigurationPatch(
            null, "new-api-key", true, "api_key", false, null, false, null);
        var exitCode = await fixture.Handler.UpdateAsync(
            "update-auth", null, null, null, null, null, null, patch,
            TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Success, exitCode);
        var reloaded = await fixture.Configurations.LoadConfigurationAsync("update-auth");
        Assert.NotNull(reloaded);
        Assert.Null(reloaded!.Authentication?.Token);
        Assert.Equal("new-api-key", reloaded.Authentication?.ApiKey);
        Assert.Equal(ProxyMode.Custom, reloaded.Proxy?.Mode);
        Assert.Equal("http://proxy.example", reloaded.Proxy?.ProxyUrl);
        Assert.Null(await fixture.SecureStorage.LoadAsync("salmonegg/config/update-auth/token"));
    }

    [Fact]
    public async Task UpdateAsync_WithAuthNone_ClearsBothCredentialKinds()
    {
        using var fixture = new HandlerFixture();
        await fixture.SeedAsync("clear-auth", "Agent", "https://agent.example", token: "old-token");
        await fixture.SecureStorage.SaveAsync("salmonegg/config/clear-auth/apiKey", "legacy-key");

        var patch = new ServerConfigurationPatch(
            null, null, true, "none", false, null, false, null);
        var exitCode = await fixture.Handler.UpdateAsync(
            "clear-auth", null, null, null, null, null, null, patch,
            TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Success, exitCode);
        var reloaded = await fixture.Configurations.LoadConfigurationAsync("clear-auth");
        Assert.NotNull(reloaded);
        Assert.Null(reloaded!.Authentication);
        Assert.Empty(fixture.SecureStorage.Keys);
    }

    [Fact]
    public async Task ListAsync_WithNoConfigurations_ReportsEmptyStateOnStdout()
    {
        using var fixture = new HandlerFixture();

        var exitCode = await fixture.Handler.ListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.Empty(fixture.Output.Errors);
        Assert.Contains("no servers configured", Assert.Single(fixture.Output.Lines), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListAsync_WithConfigurations_WritesOneLinePerServer()
    {
        using var fixture = new HandlerFixture();
        await fixture.SeedAsync("a", "Alpha", "ws://localhost:1");
        await fixture.SeedAsync("b", "Beta", "ws://localhost:2");

        var exitCode = await fixture.Handler.ListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.Equal(2, fixture.Output.Lines.Count);
        Assert.Contains(fixture.Output.Lines, line => line.Contains("a", StringComparison.Ordinal) && line.Contains("Alpha", StringComparison.Ordinal));
        Assert.Contains(fixture.Output.Lines, line => line.Contains("b", StringComparison.Ordinal) && line.Contains("Beta", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ShowAsync_WhenMissing_ReportsUsageOnStderr()
    {
        using var fixture = new HandlerFixture();

        var exitCode = await fixture.Handler.ShowAsync("absent", TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Usage, exitCode);
        Assert.Empty(fixture.Output.Lines);
        Assert.Contains("not found", Assert.Single(fixture.Output.Errors), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddAsync_WithValidWebSocketServer_PersistsAndIsListable()
    {
        using var fixture = new HandlerFixture();

        var exitCode = await fixture.Handler.AddAsync(
            "Local Agent",
            "ws://127.0.0.1:8080",
            TransportType.WebSocket,
            stdioCommand: null,
            stdioArgs: null,
            timeout: null,
            TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.Empty(fixture.Output.Errors);

        var stored = Assert.Single(await fixture.Configurations.ListConfigurationsAsync());
        Assert.Equal("Local Agent", stored.Name);
        Assert.Equal("ws://127.0.0.1:8080", stored.ServerUrl);
        Assert.Equal(AcpConnectionTimeoutPolicy.DefaultSeconds, stored.ConnectionTimeout);
    }

    [Fact]
    public async Task AddAsync_WithInvalidUrl_ReturnsUsageAndDoesNotPersist()
    {
        using var fixture = new HandlerFixture();

        var exitCode = await fixture.Handler.AddAsync(
            "Bad",
            "not-a-url",
            TransportType.WebSocket,
            stdioCommand: null,
            stdioArgs: null,
            timeout: null,
            TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Usage, exitCode);
        Assert.NotEmpty(fixture.Output.Errors);
        Assert.Empty(await fixture.Configurations.ListConfigurationsAsync());
    }

    [Fact]
    public async Task UpdateAsync_WithOnlyName_PreservesEveryOtherField()
    {
        using var fixture = new HandlerFixture();
        await fixture.SeedAsync("keep", "Original", "ws://127.0.0.1:9000", timeout: 42);

        var exitCode = await fixture.Handler.UpdateAsync(
            "keep",
            name: "Renamed",
            url: null,
            transport: null,
            stdioCommand: null,
            stdioArgs: null,
            timeout: null,
            TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Success, exitCode);
        var stored = await fixture.Configurations.LoadConfigurationAsync("keep");
        Assert.NotNull(stored);
        Assert.Equal("Renamed", stored!.Name);
        Assert.Equal("ws://127.0.0.1:9000", stored.ServerUrl);
        Assert.Equal(TransportType.WebSocket, stored.Transport);
        Assert.Equal(42, stored.ConnectionTimeout);
    }

    [Fact]
    public async Task UpdateAsync_WithoutStdioArguments_KeepsExistingArguments()
    {
        // Regression: a collection option that is absent must not be read as "clear the value".
        using var fixture = new HandlerFixture();
        await fixture.SeedStdioAsync("stdio", "Stdio Agent", "agent", ["--serve", "--mode", "plan"]);

        var exitCode = await fixture.Handler.UpdateAsync(
            "stdio",
            name: "Stdio Renamed",
            url: null,
            transport: null,
            stdioCommand: null,
            stdioArgs: null,
            timeout: null,
            TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Success, exitCode);
        var stored = await fixture.Configurations.LoadConfigurationAsync("stdio");
        Assert.NotNull(stored);
        Assert.Equal(TransportType.Stdio, stored!.Transport);
        Assert.Equal(["--serve", "--mode", "plan"], stored.StdioArguments);
    }

    [Fact]
    public async Task UpdateAsync_WithEmptyStdioArguments_ClearsArguments()
    {
        using var fixture = new HandlerFixture();
        await fixture.SeedStdioAsync("stdio", "Stdio Agent", "agent", ["--serve"]);

        var exitCode = await fixture.Handler.UpdateAsync(
            "stdio",
            name: null,
            url: null,
            transport: null,
            stdioCommand: null,
            stdioArgs: [],
            timeout: null,
            TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Success, exitCode);
        var stored = await fixture.Configurations.LoadConfigurationAsync("stdio");
        Assert.NotNull(stored);
        Assert.Empty(stored!.StdioArguments);
    }

    [Fact]
    public async Task UpdateAsync_WhenMissing_ReturnsUsage()
    {
        using var fixture = new HandlerFixture();

        var exitCode = await fixture.Handler.UpdateAsync(
            "absent",
            name: "X",
            url: null,
            transport: null,
            stdioCommand: null,
            stdioArgs: null,
            timeout: null,
            TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Usage, exitCode);
        Assert.Contains("not found", Assert.Single(fixture.Output.Errors), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RemoveAsync_WithoutConfirmation_ReturnsUsageAndKeepsConfiguration()
    {
        using var fixture = new HandlerFixture();
        await fixture.SeedAsync("keep", "Keep", "ws://127.0.0.1:1");

        var exitCode = await fixture.Handler.RemoveAsync("keep", confirmed: false, TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Usage, exitCode);
        Assert.Contains("--yes", Assert.Single(fixture.Output.Errors), StringComparison.Ordinal);
        Assert.NotNull(await fixture.Configurations.LoadConfigurationAsync("keep"));
    }

    [Fact]
    public async Task RemoveAsync_WithConfirmation_DeletesConfigurationAndCredentials()
    {
        using var fixture = new HandlerFixture();
        await fixture.SeedAsync("gone", "Gone", "ws://127.0.0.1:1", token: "secret-token");

        var exitCode = await fixture.Handler.RemoveAsync("gone", confirmed: true, TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.Null(await fixture.Configurations.LoadConfigurationAsync("gone"));
        Assert.Empty(fixture.SecureStorage.Keys);
    }

    [Fact]
    public async Task RemoveAsync_WhenMissing_ReturnsUsage()
    {
        using var fixture = new HandlerFixture();

        var exitCode = await fixture.Handler.RemoveAsync("absent", confirmed: true, TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Usage, exitCode);
        Assert.Contains("not found", Assert.Single(fixture.Output.Errors), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShowAsync_WithStoredCredential_NeverEmitsTheSecretValue()
    {
        const string secret = "super-secret-token";
        using var fixture = new HandlerFixture();
        await fixture.SeedAsync("auth", "Auth Agent", "ws://127.0.0.1:1", token: secret);

        var exitCode = await fixture.Handler.ShowAsync("auth", TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.DoesNotContain(secret, string.Join("\n", fixture.Output.Lines), StringComparison.Ordinal);
        Assert.DoesNotContain(secret, string.Join("\n", fixture.Output.Errors), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShowAsync_WithCustomProxy_WritesProxyModeAndUrl()
    {
        using var fixture = new HandlerFixture();
        await fixture.SeedAsync("proxy", "Proxy Agent", "ws://127.0.0.1:1");
        var config = await fixture.Configurations.LoadConfigurationAsync("proxy");
        Assert.NotNull(config);
        config!.Proxy = new ProxyConfig { Mode = ProxyMode.Custom, ProxyUrl = "http://proxy.example:8080" };
        await fixture.Configurations.SaveConfigurationAsync(config);
        fixture.Output.Reset();

        var exitCode = await fixture.Handler.ShowAsync("proxy", TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.Contains(fixture.Output.Lines, line => line == "proxy:      custom");
        Assert.Contains(fixture.Output.Lines, line => line == "proxy_url:  http://proxy.example:8080");
    }
}
