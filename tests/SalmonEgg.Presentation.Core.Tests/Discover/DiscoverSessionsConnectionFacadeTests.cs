using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Services.Chat;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Discover;

public sealed class DiscoverSessionsConnectionFacadeTests
{
    [Fact]
    public async Task ConnectToProfileAsync_WhenGlobalAcpDisabled_DoesNotCreateService()
    {
        var factory = new Mock<IAcpChatServiceFactory>(MockBehavior.Strict);
        var availability = new Mock<IAcpAvailabilityPolicy>();
        availability.SetupGet(x => x.IsAcpEnabled).Returns(false);
        var sut = new DiscoverSessionsConnectionFacade(
            factory.Object,
            CreateTransportSupportPolicy(supportsStdioTransport: true),
            NullLogger<DiscoverSessionsConnectionFacade>.Instance,
            availability.Object);
        var profile = new ServerConfiguration
        {
            Id = "profile-1",
            Name = "Local Agent",
            Transport = TransportType.Stdio,
            StdioCommand = "agent"
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.ConnectToProfileAsync(profile));

        Assert.Contains("ACP is disabled", ex.Message, StringComparison.Ordinal);
        Assert.False(sut.IsConnecting);
        Assert.False(sut.IsInitializing);
        Assert.False(sut.IsConnected);
        Assert.Equal(ex.Message, sut.ConnectionErrorMessage);
        factory.Verify(
            x => x.CreateChatService(
                It.IsAny<TransportType>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>()),
            Times.Never);
    }

    [Fact]
    public async Task ConnectToProfileAsync_WhenTransportUnsupported_DoesNotCreateService()
    {
        var factory = new Mock<IAcpChatServiceFactory>(MockBehavior.Strict);
        var sut = new DiscoverSessionsConnectionFacade(
            factory.Object,
            CreateTransportSupportPolicy(supportsStdioTransport: false),
            NullLogger<DiscoverSessionsConnectionFacade>.Instance);
        var profile = new ServerConfiguration
        {
            Id = "profile-1",
            Name = "Local Agent",
            Transport = TransportType.Stdio,
            StdioCommand = "agent"
        };

        var ex = await Assert.ThrowsAsync<NotSupportedException>(() =>
            sut.ConnectToProfileAsync(profile));

        Assert.Contains("Stdio transport requires", ex.Message, StringComparison.Ordinal);
        Assert.False(sut.IsConnecting);
        Assert.False(sut.IsInitializing);
        Assert.False(sut.IsConnected);
        Assert.Equal(ex.Message, sut.ConnectionErrorMessage);
        factory.Verify(
            x => x.CreateChatService(
                It.IsAny<TransportType>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>()),
            Times.Never);
    }

    private static ITransportSupportPolicy CreateTransportSupportPolicy(bool supportsStdioTransport)
        => new TransportSupportPolicy(new TestPlatformCapabilities(supportsStdioTransport));

    private sealed class TestPlatformCapabilities : IPlatformCapabilityService
    {
        public TestPlatformCapabilities(bool supportsStdioTransport)
        {
            SupportsStdioTransport = supportsStdioTransport;
        }

        public bool SupportsLaunchOnStartup => true;

        public bool SupportsTray => true;

        public bool SupportsLanguageOverride => true;

        public bool SupportsMiniWindow => true;

        public bool SupportsExternalFileOpen => true;

        public bool SupportsLocalFileExport => true;

        public bool SupportsStdioTransport { get; }

        public bool SupportsInteractiveTerminalSurface => SupportsStdioTransport;

        public bool SupportsLocalTerminal => SupportsStdioTransport;

        public bool SupportsGamepadInput => false;
    }
}
