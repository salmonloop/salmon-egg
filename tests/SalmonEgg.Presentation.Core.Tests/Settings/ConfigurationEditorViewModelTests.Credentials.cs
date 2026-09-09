using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using SalmonEgg.Application.Validators;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Tests.Localization;
using SalmonEgg.Presentation.ViewModels;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Settings;

public sealed partial class ConfigurationEditorViewModelTests
{
    [Fact]
    public async Task SaveConfigurationAsync_BlankCredentialInputPreservesIsolatedLoadedSnapshotOnRename()
    {
        var original = CreateCredentialProfile();
        var service = new Mock<IConfigurationService>();
        service.Setup(x => x.SaveConfigurationAsync(It.IsAny<ServerConfiguration>()))
            .Callback<ServerConfiguration>(candidate => candidate.PersistenceRevision = "saved-revision")
            .Returns(Task.CompletedTask);
        var editor = CreateViewModel(service);
        editor.LoadConfiguration(original);
        original.Authentication!.Token = "external-change";
        editor.Name = "Renamed agent";

        Assert.Empty(editor.Token);
        Assert.Empty(editor.ApiKey);
        await editor.SaveConfigurationAsync();

        Assert.False(editor.HasError);
        Assert.NotSame(original, editor.Configuration);
        Assert.Equal("Renamed agent", editor.Configuration.Name);
        Assert.Equal("Agent", original.Name);
        Assert.Equal("stored-canary", editor.Configuration.Authentication!.Token);
        Assert.Equal(original.CredentialBinding, editor.Configuration.CredentialBinding);
        Assert.Equal("saved-revision", editor.Configuration.PersistenceRevision);
        Assert.Empty(editor.Token);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveConfigurationAsync_EnteredCredentialReplacesStoredKindWithoutChangingBinding(bool useApiKey)
    {
        var original = CreateCredentialProfile();
        var service = new Mock<IConfigurationService>();
        var editor = CreateViewModel(service);
        editor.LoadConfiguration(original);
        editor.Token = useApiKey ? string.Empty : "replacement-canary";
        editor.ApiKey = useApiKey ? "replacement-canary" : string.Empty;

        await editor.SaveConfigurationAsync();

        Assert.False(editor.HasError);
        Assert.Equal(useApiKey ? string.Empty : "replacement-canary", editor.Configuration.Authentication!.Token);
        Assert.Equal(useApiKey ? "replacement-canary" : string.Empty, editor.Configuration.Authentication.ApiKey);
        Assert.Equal(original.CredentialBinding, editor.Configuration.CredentialBinding);
        Assert.Equal("stored-canary", original.Authentication!.Token);
        Assert.Empty(editor.Token);
        Assert.Empty(editor.ApiKey);
    }

    [Fact]
    public async Task SaveConfigurationAsync_ClearCredentialsKeepsExplicitBindingAndDoesNotEchoSecrets()
    {
        var original = CreateCredentialProfile();
        var service = new Mock<IConfigurationService>();
        var editor = CreateViewModel(service);
        editor.LoadConfiguration(original);
        editor.Token = "discard-this-entry";
        editor.ClearCredentialsOnSave = true;
        Assert.False(editor.IsCredentialInputEnabled);

        await editor.SaveConfigurationAsync();

        Assert.False(editor.HasError);
        Assert.Null(editor.Configuration.Authentication);
        Assert.Equal(original.CredentialBinding, editor.Configuration.CredentialBinding);
        Assert.Equal("stored-canary", original.Authentication!.Token);
        Assert.False(CredentialBindingResolver.Resolve(editor.Configuration).IsSuccess);
        Assert.Empty(editor.Token);
        Assert.False(editor.ClearCredentialsOnSave);
    }

    [Fact]
    public async Task SaveConfigurationAsync_BothCredentialKindsAreRejectedWithoutChangingLoadedSnapshot()
    {
        var original = CreateCredentialProfile();
        var service = new Mock<IConfigurationService>();
        var editor = CreateViewModel(service);
        editor.LoadConfiguration(original);
        editor.Name = "Uncommitted name";
        editor.Token = "new-token";
        editor.ApiKey = "new-key";

        await editor.SaveConfigurationAsync();

        Assert.True(editor.HasError);
        Assert.Equal("Agent", editor.Configuration.Name);
        Assert.Equal("stored-canary", editor.Configuration.Authentication!.Token);
        Assert.Equal("Agent", original.Name);
        service.Verify(x => x.SaveConfigurationAsync(It.IsAny<ServerConfiguration>()), Times.Never);
    }

    [Fact]
    public async Task SaveConfigurationAsync_FailureRetainsSnapshotAndDoesNotExposeSecretInDiagnostics()
    {
        const string secret = "failure-canary-secret";
        var original = CreateCredentialProfile();
        var service = new Mock<IConfigurationService>();
        service.Setup(x => x.SaveConfigurationAsync(It.IsAny<ServerConfiguration>()))
            .ThrowsAsync(new InvalidOperationException(secret));
        var logger = new Mock<ILogger<ConfigurationEditorViewModel>>();
        var editor = new ConfigurationEditorViewModel(
            new ServerConfigurationValidator(), service.Object,
            CreateTransportSupportPolicy(supportsStdioTransport: true),
            new TestCoreStringLocalizer(), logger.Object);
        editor.LoadConfiguration(original);
        editor.Name = "Uncommitted name";
        editor.Token = secret;

        await editor.SaveConfigurationAsync();

        Assert.True(editor.HasError);
        Assert.Equal("Agent", editor.Configuration.Name);
        Assert.Equal("stored-canary", editor.Configuration.Authentication!.Token);
        Assert.DoesNotContain(secret, editor.ErrorMessage, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, string.Join("\n", logger.Invocations.SelectMany(invocation =>
            invocation.Arguments.Select(argument => argument?.ToString()))), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaveConfigurationAsync_ChangedDestinationRequiresExplicitRebinding()
    {
        var original = CreateCredentialProfile();
        var service = new Mock<IConfigurationService>();
        var editor = CreateViewModel(service);
        editor.LoadConfiguration(original);
        editor.StdioCommand = "different-agent";

        await editor.SaveConfigurationAsync();

        Assert.True(editor.HasError);
        service.Verify(x => x.SaveConfigurationAsync(It.IsAny<ServerConfiguration>()), Times.Never);
        Assert.Equal("agent", editor.Configuration.StdioCommand);
        editor.BindCredentialCommand.Execute(null);
        await editor.SaveConfigurationAsync();

        Assert.False(editor.HasError);
        Assert.Equal("different-agent", editor.Configuration.StdioCommand);
        Assert.NotEqual(original.CredentialBinding!.TargetIdentity, editor.Configuration.CredentialBinding!.TargetIdentity);
        Assert.True(CredentialBindingResolver.Resolve(editor.Configuration).IsSuccess);
        service.Verify(x => x.SaveConfigurationAsync(It.IsAny<ServerConfiguration>()), Times.Once);
    }

    [Fact]
    public async Task BindCredential_ApprovesOnlyTheDestinationAtTheTimeOfTheCommand()
    {
        var service = new Mock<IConfigurationService>();
        var editor = CreateViewModel(service);
        editor.LoadConfiguration(CreateCredentialProfile());
        editor.StdioCommand = "approved-agent";
        editor.BindCredentialCommand.Execute(null);
        editor.StdioCommand = "unapproved-agent";

        await editor.SaveConfigurationAsync();

        Assert.True(editor.HasError);
        service.Verify(x => x.SaveConfigurationAsync(It.IsAny<ServerConfiguration>()), Times.Never);
    }

    [Fact]
    public async Task RemoveCredentialBinding_AllowsDestinationChangeWithoutSendingStoredCredential()
    {
        var service = new Mock<IConfigurationService>();
        var editor = CreateViewModel(service);
        editor.LoadConfiguration(CreateCredentialProfile());
        editor.StdioCommand = "different-agent";
        editor.RemoveCredentialBindingCommand.Execute(null);

        await editor.SaveConfigurationAsync();

        Assert.False(editor.HasError);
        Assert.Null(editor.Configuration.CredentialBinding);
        Assert.Equal("stored-canary", editor.Configuration.Authentication!.Token);
        var resolved = CredentialBindingResolver.Resolve(editor.Configuration);
        Assert.True(resolved.IsSuccess);
        Assert.Empty(resolved.Value!.Environment);
        Assert.False(resolved.Value.HasHeader);
    }

    [Fact]
    public void BindCredential_UnsupportedWebSocketHeadersDoNotCreateBinding()
    {
        var editor = CreateViewModel(new Mock<IConfigurationService>());
        editor.LoadBlankConfiguration();
        editor.Transport = TransportType.WebSocket;
        editor.ServerUrl = "wss://agent.example/acp";
        editor.CredentialName = "Authorization";
        editor.CredentialScheme = "Bearer";

        Assert.False(editor.BindCredentialCommand.CanExecute(null));
        editor.BindCredentialCommand.Execute(null);

        Assert.Null(editor.Configuration.CredentialBinding);
        Assert.False(editor.CanRemoveCredentialBinding);
    }

    [Fact]
    public async Task BindCredential_NewHeaderBindingUsesSelectedSourceAndScheme()
    {
        var service = new Mock<IConfigurationService>();
        var editor = CreateViewModel(service);
        editor.LoadBlankConfiguration();
        editor.Name = "HTTP agent";
        editor.Transport = TransportType.StreamableHttp;
        editor.ServerUrl = "https://agent.example/acp";
        editor.ApiKey = "new-key";
        editor.SelectedCredentialSourceOption = editor.CredentialSourceOptions.Single(option => option.Source == CredentialSource.ApiKey);
        editor.CredentialName = "X-Api-Key";
        editor.CredentialScheme = string.Empty;

        editor.BindCredentialCommand.Execute(null);
        await editor.SaveConfigurationAsync();

        Assert.False(editor.HasError);
        var resolved = CredentialBindingResolver.Resolve(editor.Configuration);
        Assert.True(resolved.IsSuccess);
        Assert.Equal("X-Api-Key", resolved.Value!.HeaderName);
        Assert.Equal("new-key", resolved.Value.HeaderValue);
    }

    [Fact]
    public async Task SaveConfigurationAsync_ConcurrentSaveDoesNotWriteASecondSnapshot()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new Mock<IConfigurationService>();
        service.Setup(x => x.SaveConfigurationAsync(It.IsAny<ServerConfiguration>())).Returns(completion.Task);
        var editor = CreateViewModel(service);
        editor.LoadConfiguration(CreateCredentialProfile());

        var first = editor.SaveConfigurationAsync();
        try
        {
            Assert.False(editor.CanSaveConfiguration);
            await editor.SaveConfigurationAsync();
            service.Verify(x => x.SaveConfigurationAsync(It.IsAny<ServerConfiguration>()), Times.Once);
        }
        finally
        {
            completion.TrySetResult();
            await first;
        }

        Assert.True(editor.CanSaveConfiguration);
    }

    [Fact]
    public void LoadBlankConfiguration_DropsPreviousCredentialAndBindingEdits()
    {
        var editor = CreateViewModel(new Mock<IConfigurationService>());
        editor.LoadConfiguration(CreateCredentialProfile());
        editor.Token = "typed-secret";
        editor.ClearCredentialsOnSave = true;

        editor.LoadBlankConfiguration();

        Assert.Null(editor.Configuration.Authentication);
        Assert.Null(editor.Configuration.CredentialBinding);
        Assert.Empty(editor.Token);
        Assert.Empty(editor.CredentialName);
        Assert.False(editor.ClearCredentialsOnSave);
        Assert.False(editor.CanRemoveCredentialBinding);
    }

    private static ServerConfiguration CreateCredentialProfile()
    {
        var profile = new ServerConfiguration
        {
            Id = "credential-profile",
            Name = "Agent",
            Transport = TransportType.Stdio,
            StdioCommand = "agent",
            PersistenceRevision = "loaded-revision",
            Authentication = new AuthenticationConfig { Token = "stored-canary" }
        };
        profile.CredentialBinding = CredentialBindingPolicy.Create(
            profile, CredentialSource.Token, CredentialTarget.Environment, "AGENT_TOKEN");
        return profile;
    }
}
