using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using SalmonEgg.Domain.Models;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Core.Tests.Localization;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public sealed partial class AcpChatCoordinatorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CredentialConnection_ClearedCredentialAfterLanguageChange_LocalizesAndRetiresOriginalService(bool pooled)
    {
        // Arrange
        var firstService = CreateChatService();
        var factory = new Mock<IAcpChatServiceFactory>();
        factory.Setup(value => value.CreateChatService(It.IsAny<ServerConfiguration>())).Returns(firstService.Object);
        var localizer = new MutableTestCoreStringLocalizer();
        localizer.Set("en-US", "CredentialBinding_MissingCredential", "Set the credential before reconnecting.");
        const string expected = "请先设置凭据，再重新连接。";
        localizer.Set("zh-Hans", "CredentialBinding_MissingCredential", expected);
        localizer.SetLanguageTag("en-US");
        var logger = new Mock<ILogger<AcpChatCoordinator>>();
        var registry = new InMemoryAcpConnectionSessionRegistry();
        var sut = new AcpChatCoordinator(factory.Object, logger.Object, CreateTransportSupportPolicy(),
            EmptyMcpServerProvider, Mock.Of<IAcpSessionCommandOrchestrator>(), sessionRegistry: registry, localizer: localizer);
        var sink = new FakeSink();
        var profile = CreateBoundProfile("old-connection-private-canary");
        await ConnectCredentialProfileAsync(sut, profile, sink, pooled);
        var cleared = profile.Clone();
        cleared.Authentication = null;

        // Act: the singleton must resolve the language when reporting this attempt's failure.
        localizer.SetLanguageTag("zh-Hans");
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => ConnectCredentialProfileAsync(sut, cleared, sink, pooled));

        // Assert
        Assert.Equal(expected, failure.Message);
        Assert.False(registry.TryGetByProfile(profile.Id, out _));
        firstService.Verify(value => value.DisconnectAsync(), Times.Once);
        firstService.Verify(value => value.Dispose(), Times.Once);
        factory.Verify(value => value.CreateChatService(It.IsAny<ServerConfiguration>()), Times.Once);
        if (!pooled) Assert.Null(sink.CurrentChatService);
        var diagnostics = failure + string.Join("\n", logger.Invocations
            .SelectMany(invocation => invocation.Arguments.Select(argument => argument?.ToString())));
        Assert.DoesNotContain("old-connection-private-canary", diagnostics, StringComparison.Ordinal);
    }
}
