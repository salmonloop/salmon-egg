using System.CommandLine;
using SalmonEgg.Cli.Commands.Config;
using SalmonEgg.Cli.Commands.Credentials;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using SalmonEgg.Infrastructure.Storage;

namespace SalmonEgg.Cli.Tests.Commands.Config;

public sealed class CredentialBindingCommandTests
{
    private const string Secret = "cli-binding-secret-canary";

    [Fact]
    public async Task ParseAndRun_ExplicitHeaderBinding_PersistsMetadataAndPrintsOnlyStatus()
    {
        using var fixture = new HandlerFixture();
        await fixture.SeedAsync("bound", "Agent", "wss://agent.example/acp", token: Secret);
        var command = ConfigServerCommandFactory.CreateServerCommand(fixture.Handler);

        var result = await command.Parse([
            "update", "bound", "--credential-source", "token", "--credential-header", "Authorization",
            "--credential-scheme", "Bearer",
        ]).InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);
        var loaded = await fixture.Configurations.LoadConfigurationAsync("bound");
        await fixture.Handler.ShowAsync("bound", TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Success, result);
        Assert.Equal("Bearer " + Secret, CredentialBindingResolver.Resolve(loaded!).Value!.HeaderValue);
        Assert.Contains("binding:    token -> header Authorization", fixture.Output.Lines);
        Assert.Contains("credential: set", fixture.Output.Lines);
        Assert.DoesNotContain(Secret, string.Join('\n', fixture.Output.Lines.Concat(fixture.Output.Errors)));
        Assert.DoesNotContain(Secret, await File.ReadAllTextAsync(
            Path.Combine(fixture.AppDataRoot, "config", "servers", "bound.yaml"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ParseAndRun_DestinationChange_RequiresExplicitNewBinding()
    {
        using var fixture = new HandlerFixture();
        await fixture.SeedAsync("bound", "Agent", "wss://agent.example/acp", token: Secret);
        var loaded = await fixture.Configurations.LoadConfigurationAsync("bound");
        loaded!.CredentialBinding = CredentialBindingPolicy.Create(loaded, CredentialSource.Token, CredentialTarget.Header, "X-Agent-Key");
        await fixture.Configurations.SaveConfigurationAsync(loaded);
        var command = ConfigServerCommandFactory.CreateServerCommand(fixture.Handler);

        var rejected = await command.Parse(["update", "bound", "--url", "wss://other.example/acp"])
            .InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Usage, rejected);
        Assert.Contains("Bind the credential again", string.Join('\n', fixture.Output.Errors), StringComparison.Ordinal);
        Assert.Equal("wss://agent.example/acp", (await fixture.Configurations.LoadConfigurationAsync("bound"))!.ServerUrl);

        var approved = await command.Parse([
            "update", "bound", "--url", "wss://other.example/acp", "--credential-source", "token",
            "--credential-header", "X-Agent-Key",
        ]).InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Success, approved);
        Assert.True(CredentialBindingResolver.Resolve((await fixture.Configurations.LoadConfigurationAsync("bound"))!).IsSuccess);
    }

    [Fact]
    public async Task ParseAndRun_InvalidHeader_ShowsActionableDiagnosticWithoutWriting()
    {
        // Arrange
        using var fixture = new HandlerFixture();
        await fixture.SeedAsync("bound", "Agent", "wss://agent.example/acp", token: Secret);
        var command = ConfigServerCommandFactory.CreateServerCommand(fixture.Handler);

        // Act
        var result = await command.Parse(["update", "bound", "--credential-source", "token", "--credential-header", "Host"])
            .InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(CliExitCodes.Usage, result);
        Assert.Contains("Choose an authentication header name", string.Join('\n', fixture.Output.Errors), StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, string.Join('\n', fixture.Output.Errors), StringComparison.Ordinal);
        Assert.Null((await fixture.Configurations.LoadConfigurationAsync("bound"))!.CredentialBinding);
    }

    [Fact]
    public async Task ClearAndReplaceCredential_PreserveBindingWithoutFallingBack()
    {
        using var fixture = new HandlerFixture();
        await fixture.SeedStdioAsync("bound", "Agent", "agent", []);
        var loaded = await fixture.Configurations.LoadConfigurationAsync("bound");
        loaded!.Authentication = new AuthenticationConfig { Token = Secret };
        loaded.CredentialBinding = CredentialBindingPolicy.Create(loaded, CredentialSource.Token, CredentialTarget.Environment, "AGENT_TOKEN");
        await fixture.Configurations.SaveConfigurationAsync(loaded);
        var credentials = new CredentialsHandler(fixture.Output, fixture.Configurations, new ServerCredentialService(fixture.SecureStorage));

        Assert.Equal(CliExitCodes.Success, await credentials.ClearAsync("bound", TestContext.Current.CancellationToken));
        var cleared = await fixture.Configurations.LoadConfigurationAsync("bound");
        Assert.Equal(loaded.CredentialBinding, cleared!.CredentialBinding);
        Assert.False(CredentialBindingResolver.Resolve(cleared).IsSuccess);

        Assert.Equal(CliExitCodes.Success, await credentials.SetAsync("bound", "replacement", null, TestContext.Current.CancellationToken));
        var updated = await fixture.Configurations.LoadConfigurationAsync("bound");
        Assert.Equal(loaded.CredentialBinding, updated!.CredentialBinding);
        Assert.Equal("replacement", CredentialBindingResolver.Resolve(updated).Value!.Environment["AGENT_TOKEN"]);

        var command = ConfigServerCommandFactory.CreateServerCommand(fixture.Handler);
        Assert.Equal(CliExitCodes.Success, await command.Parse(["update", "bound", "--clear-credential-binding"])
            .InvokeAsync(cancellationToken: TestContext.Current.CancellationToken));
        var unbound = await fixture.Configurations.LoadConfigurationAsync("bound");
        Assert.Null(unbound!.CredentialBinding);
        Assert.Equal("replacement", unbound.Authentication!.Token);
    }

    [Theory]
    [InlineData("--credential-header", "Authorization")]
    [InlineData("--credential-source", "token")]
    [InlineData("--credential-scheme", "Bearer")]
    public async Task ParseAndRun_IncompleteBinding_ReturnsUsageWithoutWriting(string option, string value)
    {
        using var fixture = new HandlerFixture();
        await fixture.SeedAsync("bound", "Agent", "wss://agent.example/acp", token: Secret);
        var command = ConfigServerCommandFactory.CreateServerCommand(fixture.Handler);

        var result = await command.Parse(["update", "bound", option, value])
            .InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Usage, result);
        Assert.Null((await fixture.Configurations.LoadConfigurationAsync("bound"))!.CredentialBinding);
        Assert.DoesNotContain(Secret, string.Join('\n', fixture.Output.Errors));
    }
}
