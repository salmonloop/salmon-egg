using FluentValidation;
using Microsoft.Extensions.Logging;
using Moq;
using SalmonEgg.Application.Validators;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.ViewModels;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Settings;

public sealed class ConfigurationEditorViewModelTests
{
    [Fact]
    public void TransportOptions_Should_PresentStdioAsSubprocessTransport()
    {
        var validator = new ServerConfigurationValidator();
        var configurationService = new Mock<IConfigurationService>();
        var transportSupportPolicy = CreateTransportSupportPolicy(supportsStdioTransport: true);
        var logger = new Mock<ILogger<ConfigurationEditorViewModel>>();
        var viewModel = new ConfigurationEditorViewModel(
            validator,
            configurationService.Object,
            transportSupportPolicy,
            logger.Object);

        Assert.Equal("Stdio（子进程）", viewModel.TransportOptions[0].Name);
    }

    [Fact]
    public void TransportOptions_Should_HideStdio_WhenSubprocessTransportUnsupported()
    {
        var validator = new ServerConfigurationValidator();
        var configurationService = new Mock<IConfigurationService>();
        var transportSupportPolicy = CreateTransportSupportPolicy(supportsStdioTransport: false);
        var logger = new Mock<ILogger<ConfigurationEditorViewModel>>();
        var viewModel = new ConfigurationEditorViewModel(
            validator,
            configurationService.Object,
            transportSupportPolicy,
            logger.Object);

        Assert.DoesNotContain(viewModel.TransportOptions, option => option.Type == TransportType.Stdio);
        Assert.Equal(TransportType.WebSocket, viewModel.TransportOptions[0].Type);
    }

    [Fact]
    public void LoadBlankConfiguration_Should_DefaultToWebSocket_WhenStdioUnsupported()
    {
        var validator = new ServerConfigurationValidator();
        var configurationService = new Mock<IConfigurationService>();
        var transportSupportPolicy = CreateTransportSupportPolicy(supportsStdioTransport: false);
        var logger = new Mock<ILogger<ConfigurationEditorViewModel>>();
        var viewModel = new ConfigurationEditorViewModel(
            validator,
            configurationService.Object,
            transportSupportPolicy,
            logger.Object);

        viewModel.LoadBlankConfiguration();

        Assert.Equal(TransportType.WebSocket, viewModel.Transport);
        Assert.Equal(TransportType.WebSocket, viewModel.Configuration.Transport);
        Assert.False(viewModel.IsStdio);
    }

    [Fact]
    public void LoadConfiguration_Should_CoerceStdioToWebSocket_WhenStdioUnsupported()
    {
        var validator = new ServerConfigurationValidator();
        var configurationService = new Mock<IConfigurationService>();
        var transportSupportPolicy = CreateTransportSupportPolicy(supportsStdioTransport: false);
        var logger = new Mock<ILogger<ConfigurationEditorViewModel>>();
        var viewModel = new ConfigurationEditorViewModel(
            validator,
            configurationService.Object,
            transportSupportPolicy,
            logger.Object);

        viewModel.LoadConfiguration(new ServerConfiguration
        {
            Id = "profile-1",
            Name = "Local Agent",
            Transport = TransportType.Stdio,
            StdioCommand = "agent",
            StdioArgs = "--stdio"
        });

        Assert.Equal(TransportType.WebSocket, viewModel.Transport);
        Assert.Equal(TransportType.WebSocket, viewModel.SelectedTransportOption?.Type);
        Assert.False(viewModel.IsStdio);
    }

    [Fact]
    public void LoadConfiguration_Should_ProjectSystemProxyMode()
    {
        var validator = new ServerConfigurationValidator();
        var configurationService = new Mock<IConfigurationService>();
        var transportSupportPolicy = CreateTransportSupportPolicy(supportsStdioTransport: true);
        var logger = new Mock<ILogger<ConfigurationEditorViewModel>>();
        var viewModel = new ConfigurationEditorViewModel(
            validator,
            configurationService.Object,
            transportSupportPolicy,
            logger.Object);

        viewModel.LoadConfiguration(new ServerConfiguration
        {
            Id = "profile-2",
            Name = "Remote Agent",
            Transport = TransportType.WebSocket,
            ServerUrl = "ws://example.com/acp/ws",
            Proxy = new ProxyConfig
            {
                Mode = ProxyMode.System,
                ProxyUrl = string.Empty
            }
        });

        Assert.Equal(ProxyMode.System, viewModel.ProxyMode);
        Assert.False(viewModel.IsCustomProxy);
        Assert.Equal(string.Empty, viewModel.ProxyUrl);
    }

    [Fact]
    public async Task SaveConfigurationAsync_Should_PersistCustomProxyMode()
    {
        var validator = new ServerConfigurationValidator();
        var configurationService = new Mock<IConfigurationService>();
        var transportSupportPolicy = CreateTransportSupportPolicy(supportsStdioTransport: true);
        var logger = new Mock<ILogger<ConfigurationEditorViewModel>>();
        var viewModel = new ConfigurationEditorViewModel(
            validator,
            configurationService.Object,
            transportSupportPolicy,
            logger.Object);

        viewModel.LoadBlankConfiguration();
        viewModel.Name = "Remote Agent";
        viewModel.Transport = TransportType.WebSocket;
        viewModel.ServerUrl = "ws://example.com/acp/ws";
        viewModel.ProxyMode = ProxyMode.Custom;
        viewModel.ProxyUrl = "http://proxy.example.com:8080";

        await viewModel.SaveConfigurationAsync();

        configurationService.Verify(
            x => x.SaveConfigurationAsync(It.Is<ServerConfiguration>(config =>
                config.Proxy != null
                && config.Proxy.Mode == ProxyMode.Custom
                && config.Proxy.ProxyUrl == "http://proxy.example.com:8080")),
            Times.Once);
    }

    private static Mock<IPlatformCapabilityService> CreateCapabilities(bool supportsStdioTransport)
    {
        var capabilities = new Mock<IPlatformCapabilityService>();
        capabilities.SetupGet(c => c.SupportsStdioTransport).Returns(supportsStdioTransport);
        capabilities.SetupGet(c => c.SupportsLocalTerminal).Returns(true);
        capabilities.SetupGet(c => c.SupportsInteractiveTerminalSurface).Returns(true);
        return capabilities;
    }

    private static ITransportSupportPolicy CreateTransportSupportPolicy(bool supportsStdioTransport)
        => new TransportSupportPolicy(CreateCapabilities(supportsStdioTransport).Object);
}
