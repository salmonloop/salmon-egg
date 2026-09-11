using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Resources;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Moq;
using SalmonEgg.Acp.Mcp;
using SalmonEgg.Application.Services.Mcp;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Resources;
using SalmonEgg.Presentation.Core.Services.Chat;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public partial class ChatViewModelTests
{
    private const string ConnectionCredentialCanary = "credential-connection-private-canary";

    [Theory]
    [InlineData(CredentialBindingValidationError.MissingCredential, "尚未设置已绑定的凭据。请先保存 Token 或 API Key，或移除凭据绑定后再连接。")]
    [InlineData(CredentialBindingValidationError.UnsupportedCredentialCharacters, "凭据包含目标不支持的字符。请替换已保存的凭据后再连接。")]
    [InlineData(CredentialBindingValidationError.DestinationChanged, "配置目标已更改。请重新绑定凭据，确认允许将凭据发送到新目标。")]
    [InlineData(CredentialBindingValidationError.ReservedEnvironmentName, "PATH 和 PATHEXT 由启动器管理。请选择 Agent 用于接收凭据的环境变量。")]
    public async Task CredentialConnection_Failure_ProjectsChineseRemediationWithoutLaunching(
        CredentialBindingValidationError error, string expected)
    {
        // Arrange: invoke the real connection owner from its public view-model command.
        var capabilities = new Mock<IPlatformCapabilityService>();
        capabilities.SetupGet(value => value.SupportsStdioTransport).Returns(true);
        var factory = new Mock<IAcpChatServiceFactory>(MockBehavior.Strict);
        var provider = new Mock<IAcpMcpServerProvider>();
        provider.Setup(value => value.GetMcpServersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<McpServer>)Array.Empty<McpServer>());
        var localizer = new ConnectionCredentialResourceLocalizer();
        var logger = new Mock<ILogger<AcpChatCoordinator>>();
        var coordinator = new AcpChatCoordinator(factory.Object, logger.Object,
            new TransportSupportPolicy(capabilities.Object), provider.Object,
            Mock.Of<IAcpSessionCommandOrchestrator>(), localizer: localizer);
        await using var fixture = CreateViewModel(acpConnectionCommands: coordinator, localizer: localizer,
            chatStateProjector: new ChatStateProjector(localizer));
        var profile = CreateInvalidConnectionCredentialProfile(error);
        fixture.Profiles.Profiles.Add(profile);

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.ViewModel.ConnectToAcpProfileCommand.ExecuteAsync(profile));
        var state = await fixture.GetConnectionStateAsync();
        await WaitForConditionAsync(() => Task.FromResult(fixture.ViewModel.HasConnectionError));

        // Assert
        Assert.Equal(expected, failure.Message);
        Assert.Equal(ConnectionPhase.Disconnected, state.Phase);
        Assert.Equal(expected, state.Error);
        Assert.Equal(expected, fixture.ViewModel.ConnectionErrorMessage);
        Assert.Equal(localizer["ChatConnectionStatus_Disconnected"].Value, fixture.ViewModel.CurrentConnectionStatus);
        factory.Verify(value => value.CreateChatService(It.IsAny<ServerConfiguration>()), Times.Never);
        var diagnostics = failure + "\n" + string.Join("\n", logger.Invocations.Concat(fixture.ViewModelLogger.Invocations)
            .SelectMany(invocation => invocation.Arguments.Select(argument => argument?.ToString())));
        Assert.DoesNotContain(ConnectionCredentialCanary, diagnostics, StringComparison.Ordinal);
    }

    private static ServerConfiguration CreateInvalidConnectionCredentialProfile(CredentialBindingValidationError error)
    {
        var profile = new ServerConfiguration
        {
            Id = "localized-credential",
            Name = "已绑定配置",
            Transport = TransportType.Stdio,
            StdioCommand = "agent",
            Authentication = error == CredentialBindingValidationError.MissingCredential ? null
                : new AuthenticationConfig
                {
                    Token = ConnectionCredentialCanary + (error == CredentialBindingValidationError.UnsupportedCredentialCharacters ? "\0" : "")
                }
        };
        profile.CredentialBinding = CredentialBindingPolicy.Create(profile, CredentialSource.Token, CredentialTarget.Environment,
            error == CredentialBindingValidationError.ReservedEnvironmentName ? "PATH" : "AGENT_TOKEN");
        if (error == CredentialBindingValidationError.DestinationChanged) profile.StdioCommand = "different-agent";
        return profile;
    }

    private sealed class ConnectionCredentialResourceLocalizer : IStringLocalizer<CoreStrings>
    {
        private static readonly ResourceManager Resources = new(typeof(CoreStrings));

        public LocalizedString this[string name] => new(name,
            Resources.GetString(name, CultureInfo.GetCultureInfo("zh-Hans")) ?? name);

        public LocalizedString this[string name, params object[] arguments] => this[name];

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
