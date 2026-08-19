using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
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
    public async Task UpdateAsync_WithCustomProxyModeAndNoUrl_PreservesExistingCustomUrl()
    {
        using var fixture = new HandlerFixture();
        await fixture.SeedAsync("proxy-preserve", "Proxy Agent", "ws://127.0.0.1:1");
        var config = await fixture.Configurations.LoadConfigurationAsync("proxy-preserve");
        Assert.NotNull(config);
        config!.Proxy = new ProxyConfig { Mode = ProxyMode.Custom, ProxyUrl = "http://existing-proxy.example:8080" };
        await fixture.Configurations.SaveConfigurationAsync(config);
        fixture.Output.Reset();

        var patch = new ServerConfigurationPatch(null, null, false, null, true, ProxyMode.Custom, false, null);
        var exitCode = await fixture.Handler.UpdateAsync(
            "proxy-preserve", null, null, null, null, null, null, patch,
            TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Success, exitCode);
        var updated = await fixture.Configurations.LoadConfigurationAsync("proxy-preserve");
        Assert.NotNull(updated);
        Assert.Equal(ProxyMode.Custom, updated!.Proxy?.Mode);
        Assert.Equal("http://existing-proxy.example:8080", updated.Proxy?.ProxyUrl);
    }

    [Fact]
    public async Task UpdateAsync_WithProxyUrlOnly_UsesExistingCustomProxyMode()
    {
        using var fixture = new HandlerFixture();
        await fixture.SeedAsync("proxy-url-only", "Proxy Agent", "ws://127.0.0.1:1");
        var config = await fixture.Configurations.LoadConfigurationAsync("proxy-url-only");
        Assert.NotNull(config);
        config!.Proxy = new ProxyConfig { Mode = ProxyMode.Custom, ProxyUrl = "http://old-proxy.example:8080" };
        await fixture.Configurations.SaveConfigurationAsync(config);
        fixture.Output.Reset();

        var patch = new ServerConfigurationPatch(null, null, false, null, false, null, true, "http://new-proxy.example:8080");
        var exitCode = await fixture.Handler.UpdateAsync(
            "proxy-url-only", null, null, null, null, null, null, patch,
            TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Success, exitCode);
        var updated = await fixture.Configurations.LoadConfigurationAsync("proxy-url-only");
        Assert.NotNull(updated);
        Assert.Equal(ProxyMode.Custom, updated!.Proxy?.Mode);
        Assert.Equal("http://new-proxy.example:8080", updated.Proxy?.ProxyUrl);
    }

    [Fact]
    public async Task UpdateAsync_WithNonCustomProxyMode_ClearsExistingCustomUrl()
    {
        using var fixture = new HandlerFixture();
        await fixture.SeedAsync("proxy-system", "Proxy Agent", "ws://127.0.0.1:1");
        var config = await fixture.Configurations.LoadConfigurationAsync("proxy-system");
        Assert.NotNull(config);
        config!.Proxy = new ProxyConfig { Mode = ProxyMode.Custom, ProxyUrl = "http://custom-proxy.example:8080" };
        await fixture.Configurations.SaveConfigurationAsync(config);
        fixture.Output.Reset();

        var patch = new ServerConfigurationPatch(null, null, false, null, true, ProxyMode.System, false, null);
        var exitCode = await fixture.Handler.UpdateAsync(
            "proxy-system", null, null, null, null, null, null, patch,
            TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Success, exitCode);
        var updated = await fixture.Configurations.LoadConfigurationAsync("proxy-system");
        Assert.NotNull(updated);
        Assert.Equal(ProxyMode.System, updated!.Proxy?.Mode);
        Assert.Null(updated.Proxy?.ProxyUrl);
    }

    [Fact]
    public async Task AddAsync_WithCustomProxyWithoutUrl_ReturnsUsage()
    {
        using var fixture = new HandlerFixture();
        var patch = new ServerConfigurationPatch(null, null, false, null, true, ProxyMode.Custom, false, null);

        var exitCode = await fixture.Handler.AddAsync(
            "Proxy Agent", "ws://127.0.0.1:1", TransportType.WebSocket, null, null, null, patch,
            TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Usage, exitCode);
        Assert.Equal("Custom proxy mode requires --proxy-url.", Assert.Single(fixture.Output.Errors));
    }

    [Fact]
    public async Task ListAsync_WhenConfigurationLoadFails_ReturnsFailureWithContextualDiagnostic()
    {
        var output = new RecordingCliOutput();
        var handler = new ServerConfigurationHandler(
            new ThrowingConfigurationService(),
            new ServerConfigurationValidator(),
            output);

        var exitCode = await handler.ListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Failure, exitCode);
        Assert.Equal("List failed: Configuration data is temporarily unavailable.", Assert.Single(output.Errors));
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
    public async Task ShowAsync_WhenMissing_ReportsFailureOnStderr()
    {
        using var fixture = new HandlerFixture();

        var exitCode = await fixture.Handler.ShowAsync("absent", TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Failure, exitCode);
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
    public async Task UpdateAsync_WhenMissing_ReturnsFailure()
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

        Assert.Equal(CliExitCodes.Failure, exitCode);
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
    public async Task RemoveAsync_WhenMissing_ReturnsFailure()
    {
        using var fixture = new HandlerFixture();

        var exitCode = await fixture.Handler.RemoveAsync("absent", confirmed: true, TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Failure, exitCode);
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

    [Fact]
    public async Task ShowAsync_WhenConfigFileLocked_ReturnsFailureNotNotFound()
    {
        // 文件被占用时 LoadConfigurationAsync 抛 ConfigurationReadFailed,handler 必须映射为
        // Failure(1) + 可重试文案,而非误报 "Server not found"(Usage=2)——否则用户会以为服务器不存在,
        // 而 remove 还会在锁释放后真的删掉它刚声称不存在的服务器。
        using var fixture = new HandlerFixture();
        await fixture.SeedAsync("locked-show", "Locked Agent", "ws://127.0.0.1:1");
        var yamlPath = Path.Combine(fixture.AppDataRoot, "config", "servers", "locked-show.yaml");

        using var lockStream = new FileStream(
            yamlPath, FileMode.Open, FileAccess.Read, FileShare.None);
        fixture.Output.Reset();

        var exitCode = await fixture.Handler.ShowAsync("locked-show", TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Failure, exitCode);
        Assert.NotEmpty(fixture.Output.Errors);
        Assert.DoesNotContain(fixture.Output.Errors, line => line.Contains("not found", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RemoveAsync_WhenConfigFileLocked_ReturnsFailureAndRetainsServer()
    {
        // remove 先 load 再 delete。load 阶段被锁阻断必须报 Failure,且服务器不得被删除;
        // 锁释放后重试 remove 应成功(idempotent),真正删除一个它刚才报"I/O 失败"而非"不存在"的服务器。
        using var fixture = new HandlerFixture();
        await fixture.SeedAsync("locked-remove", "Locked Remove", "ws://127.0.0.1:2");
        var yamlPath = Path.Combine(fixture.AppDataRoot, "config", "servers", "locked-remove.yaml");

        FileStream lockStream;
        try
        {
            lockStream = new FileStream(yamlPath, FileMode.Open, FileAccess.Read, FileShare.None);
        }
        catch (IOException)
        {
            // 上一轮测试残留锁或文件未就绪时跳过,避免假阳性。
            return;
        }

        using (lockStream)
        {
            fixture.Output.Reset();
            var lockedExit = await fixture.Handler.RemoveAsync("locked-remove", confirmed: true, TestContext.Current.CancellationToken);
            Assert.Equal(CliExitCodes.Failure, lockedExit);
            Assert.DoesNotContain(fixture.Output.Errors, line => line.Contains("not found", StringComparison.Ordinal));
            // 服务器仍存在:用文件存在性而非 LoadConfigurationAsync 验证,因为锁仍持有,
            // 后者会再次被 I/O 阻断(这正是该测试要证明的暂态故障行为)。
            Assert.True(File.Exists(yamlPath));
        }

        // 锁释放后重试成功。
        var retryExit = await fixture.Handler.RemoveAsync("locked-remove", confirmed: true, TestContext.Current.CancellationToken);
        Assert.Equal(CliExitCodes.Success, retryExit);
        Assert.Null(await fixture.Configurations.LoadConfigurationAsync("locked-remove"));
    }

    private sealed class ThrowingConfigurationService : IConfigurationService
    {
        private static ConfigurationPersistenceException CreateException() =>
            new(
                ConfigurationPersistenceFailureReason.ConfigurationReadFailed,
                "Configuration data is temporarily unavailable.");

        public Task SaveConfigurationAsync(ServerConfiguration config) => throw CreateException();

        public Task<ServerConfiguration?> LoadConfigurationAsync(string id) => throw CreateException();

        public Task<IEnumerable<ServerConfiguration>> ListConfigurationsAsync() => throw CreateException();

        public Task DeleteConfigurationAsync(string id, string? expectedRevision = null) => throw CreateException();
    }
}
