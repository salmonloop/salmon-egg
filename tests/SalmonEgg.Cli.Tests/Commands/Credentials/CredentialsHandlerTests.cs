using System;
using System.IO;
using System.Threading.Tasks;
using SalmonEgg.Cli.Commands.Credentials;
using SalmonEgg.Cli.Hosting;
using SalmonEgg.Cli.Tests.Commands.Config;
using SalmonEgg.Domain.Services;
using SalmonEgg.Infrastructure.Storage;

namespace SalmonEgg.Cli.Tests.Commands.Credentials;

public sealed class CredentialsHandlerTests
{
    [Fact]
    public async Task SetAsync_WithToken_StoresItOutsideYamlAndDoesNotEchoIt()
    {
        using var fixture = new HandlerFixture();
        await fixture.SeedAsync("server-token", "Token server", "ws://token.example");
        var handler = CreateHandler(fixture);
        const string token = "token-value-must-not-be-output";

        var exitCode = await handler.SetAsync(
            "server-token",
            token,
            null,
            TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.Equal(token, await fixture.SecureStorage.LoadAsync("salmonegg/config/server-token/token"));
        Assert.Null(await fixture.SecureStorage.LoadAsync("salmonegg/config/server-token/apiKey"));
        Assert.Equal("Token saved for server 'server-token'.", Assert.Single(fixture.Output.Lines));
        Assert.DoesNotContain(token, string.Join(Environment.NewLine, fixture.Output.Lines), StringComparison.Ordinal);

        var yamlPath = Path.Combine(fixture.AppDataRoot, "config", "servers", "server-token.yaml");
        var yaml = await File.ReadAllTextAsync(yamlPath, TestContext.Current.CancellationToken);
        Assert.DoesNotContain(token, yaml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SetAsync_WithApiKey_StoresItAndHasReportsOnlyPresence()
    {
        using var fixture = new HandlerFixture();
        await fixture.SeedAsync("server-api-key", "API key server", "ws://api-key.example");
        var handler = CreateHandler(fixture);
        const string apiKey = "api-key-value-must-not-be-output";

        var setExitCode = await handler.SetAsync(
            "server-api-key",
            null,
            apiKey,
            TestContext.Current.CancellationToken);
        var hasExitCode = await handler.HasAsync("server-api-key", TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Success, setExitCode);
        Assert.Equal(CliExitCodes.Success, hasExitCode);
        Assert.Null(await fixture.SecureStorage.LoadAsync("salmonegg/config/server-api-key/token"));
        Assert.Equal(apiKey, await fixture.SecureStorage.LoadAsync("salmonegg/config/server-api-key/apiKey"));
        Assert.Contains("token: absent", fixture.Output.Lines);
        Assert.Contains("api_key: present", fixture.Output.Lines);
        Assert.DoesNotContain(apiKey, string.Join(Environment.NewLine, fixture.Output.Lines), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SetAsync_WithBothCredentialKinds_RejectsTheRequestWithoutWriting()
    {
        using var fixture = new HandlerFixture();
        await fixture.SeedAsync("server-conflict", "Conflict server", "ws://conflict.example");
        var handler = CreateHandler(fixture);
        const string token = "token-not-written";
        const string apiKey = "api-key-not-written";

        var exitCode = await handler.SetAsync(
            "server-conflict",
            token,
            apiKey,
            TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Usage, exitCode);
        Assert.Empty(fixture.SecureStorage.Keys);
        Assert.Equal("Specify exactly one credential input.", Assert.Single(fixture.Output.Errors));
        Assert.DoesNotContain(token, fixture.Output.Errors[0], StringComparison.Ordinal);
        Assert.DoesNotContain(apiKey, fixture.Output.Errors[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task SetFromStdinAsync_WithEndOfInput_ReportsEmptyCredentialWithoutWriting()
    {
        using var fixture = new HandlerFixture();
        await fixture.SeedAsync("server-eof", "EOF server", "ws://eof.example");
        var handler = new CredentialsHandler(
            fixture.Output,
            fixture.Configurations,
            new ServerCredentialService(fixture.SecureStorage),
            new TextCliInput(new StringReader(string.Empty)));

        var exitCode = await handler.SetFromStdinAsync(
            "server-eof",
            tokenFromStdin: true,
            apiKeyFromStdin: false,
            TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Usage, exitCode);
        Assert.Empty(fixture.SecureStorage.Keys);
        Assert.Equal("Credential value cannot be empty.", Assert.Single(fixture.Output.Errors));
    }

    [Fact]
    public async Task ClearAsync_RemovesBothCredentialKinds()
    {
        using var fixture = new HandlerFixture();
        await fixture.SeedAsync("server-clear", "Clear server", "ws://clear.example");
        var config = await fixture.Configurations.LoadConfigurationAsync("server-clear");
        Assert.NotNull(config);
        config!.Authentication = new SalmonEgg.Domain.Models.AuthenticationConfig { Token = "token-value" };
        await fixture.Configurations.SaveConfigurationAsync(config);
        await fixture.SecureStorage.SaveAsync("salmonegg/config/server-clear/apiKey", "api-key-value");
        var credentials = new ServerCredentialService(fixture.SecureStorage);
        var handler = new CredentialsHandler(fixture.Output, fixture.Configurations, credentials);

        var exitCode = await handler.ClearAsync("server-clear", TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.Empty(fixture.SecureStorage.Keys);
        Assert.Equal("Credentials cleared for server 'server-clear'.", Assert.Single(fixture.Output.Lines));
    }

    [Fact]
    public async Task HasAsync_ForMissingServer_DoesNotAccessCredentialStorage()
    {
        using var fixture = new HandlerFixture();
        var handler = CreateHandler(fixture);

        var exitCode = await handler.HasAsync("missing", TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Failure, exitCode);
        Assert.Empty(fixture.SecureStorage.Keys);
        Assert.Equal("Server 'missing' not found.", Assert.Single(fixture.Output.Errors));
    }

    [Fact]
    public async Task HasAsync_WhenSecureStorageUnavailable_ReturnsFailureWithoutRawExceptionType()
    {
        using var fixture = new HandlerFixture();
        await fixture.SeedAsync("server-status-failure", "Status server", "ws://status.example");
        var handler = new CredentialsHandler(
            fixture.Output,
            fixture.Configurations,
            new UnavailableCredentialService());

        var exitCode = await handler.HasAsync("server-status-failure", TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Failure, exitCode);
        var error = Assert.Single(fixture.Output.Errors);
        Assert.Contains("Credential status failed:", error, StringComparison.Ordinal);
        Assert.Contains("Secure storage is unavailable", error, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", error, StringComparison.Ordinal);
    }

    private static CredentialsHandler CreateHandler(HandlerFixture fixture) =>
        new(fixture.Output, fixture.Configurations, new ServerCredentialService(fixture.SecureStorage));

    private sealed class UnavailableCredentialService : IServerCredentialService
    {
        public Task<ServerCredentialStatus> GetStatusAsync(string serverId)
            => throw new ConfigurationPersistenceException(
                ConfigurationPersistenceFailureReason.SecureStorageUnavailable,
                "Secure storage is unavailable; credential status could not be read.");
    }
}
