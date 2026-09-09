using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Services.Chat;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public sealed partial class AcpChatCoordinatorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConnectProfile_UnchangedHydratedCredentialSnapshotReusesSession(bool pooled)
    {
        var service = CreateChatService();
        var factory = new Mock<IAcpChatServiceFactory>();
        factory.Setup(x => x.CreateChatService(It.IsAny<ServerConfiguration>())).Returns(service.Object);
        var sut = CreateCoordinator(factory.Object, Mock.Of<ILogger<AcpChatCoordinator>>(), CreateTransportSupportPolicy(), EmptyMcpServerProvider);
        var sink = new FakeSink();
        var profile = CreateBoundProfile("stable-canary");
        profile.PersistenceRevision = null;

        var first = await ConnectCredentialProfileAsync(sut, profile, sink, pooled);
        var second = await ConnectCredentialProfileAsync(sut, profile.Clone(), sink, pooled);

        Assert.Same(first.ChatService, second.ChatService);
        factory.Verify(x => x.CreateChatService(It.IsAny<ServerConfiguration>()), Times.Once);
        service.Verify(x => x.DisconnectAsync(), Times.Never);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConnectProfile_SecretImportWithUnchangedRevisionReplacesOldSession(bool pooled)
    {
        var firstService = CreateChatService();
        var secondService = CreateChatService();
        var factory = new Mock<IAcpChatServiceFactory>();
        factory.SetupSequence(x => x.CreateChatService(It.IsAny<ServerConfiguration>()))
            .Returns(firstService.Object).Returns(secondService.Object);
        var logger = new Mock<ILogger<AcpChatCoordinator>>();
        var registry = new InMemoryAcpConnectionSessionRegistry();
        var sut = CreateCoordinator(factory.Object, logger.Object, CreateTransportSupportPolicy(), EmptyMcpServerProvider, sessionRegistry: registry);
        var sink = new FakeSink();
        var profile = CreateBoundProfile("old-secret-canary");
        var first = await ConnectCredentialProfileAsync(sut, profile, sink, pooled);
        var imported = profile.Clone();
        imported.Authentication!.Token = "imported-secret-canary";

        var second = await ConnectCredentialProfileAsync(sut, imported, sink, pooled);

        Assert.NotSame(first.ChatService, second.ChatService);
        firstService.Verify(x => x.DisconnectAsync(), Times.Once);
        firstService.Verify(x => x.Dispose(), Times.Once);
        factory.Verify(x => x.CreateChatService(It.Is<ServerConfiguration>(candidate =>
            candidate.Authentication!.Token == "imported-secret-canary"
            && candidate.PersistenceRevision == profile.PersistenceRevision)), Times.Once);
        Assert.True(registry.TryGetByProfile(profile.Id, out var session));
        var diagnostics = session.ToString() + "\n" + string.Join("\n", logger.Invocations.SelectMany(invocation =>
            invocation.Arguments.Select(argument => argument?.ToString())));
        Assert.DoesNotContain("old-secret-canary", diagnostics, StringComparison.Ordinal);
        Assert.DoesNotContain("imported-secret-canary", diagnostics, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConnectProfile_ClearedCredentialRetiresOldSessionWithoutCreatingAnother(bool pooled)
    {
        var service = CreateChatService();
        var factory = new Mock<IAcpChatServiceFactory>();
        factory.Setup(x => x.CreateChatService(It.IsAny<ServerConfiguration>())).Returns(service.Object);
        var registry = new InMemoryAcpConnectionSessionRegistry();
        var sut = CreateCoordinator(factory.Object, Mock.Of<ILogger<AcpChatCoordinator>>(), CreateTransportSupportPolicy(), EmptyMcpServerProvider, sessionRegistry: registry);
        var sink = new FakeSink();
        var profile = CreateBoundProfile("clear-canary");
        await ConnectCredentialProfileAsync(sut, profile, sink, pooled);
        var cleared = profile.Clone();
        cleared.Authentication = null;

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ConnectCredentialProfileAsync(sut, cleared, sink, pooled));

        Assert.Contains("not set", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("clear-canary", failure.Message, StringComparison.Ordinal);
        Assert.False(registry.TryGetByProfile(profile.Id, out _));
        if (!pooled)
        {
            Assert.Null(sink.CurrentChatService);
        }

        service.Verify(x => x.DisconnectAsync(), Times.Once);
        service.Verify(x => x.Dispose(), Times.Once);
        factory.Verify(x => x.CreateChatService(It.IsAny<ServerConfiguration>()), Times.Once);
    }

    [Theory]
    [InlineData(false, "endpoint")]
    [InlineData(true, "endpoint")]
    [InlineData(false, "path")]
    [InlineData(true, "path")]
    [InlineData(false, "command")]
    [InlineData(true, "command")]
    [InlineData(false, "arguments")]
    [InlineData(true, "arguments")]
    public async Task ConnectProfile_DestinationChangeWithoutRebindingFailsClosed(bool pooled, string change)
    {
        var service = CreateChatService();
        var factory = new Mock<IAcpChatServiceFactory>();
        factory.Setup(x => x.CreateChatService(It.IsAny<ServerConfiguration>())).Returns(service.Object);
        var sut = CreateCoordinator(factory.Object, Mock.Of<ILogger<AcpChatCoordinator>>(), CreateTransportSupportPolicy(), EmptyMcpServerProvider);
        var sink = new FakeSink();
        var profile = CreateBoundProfile("destination-canary", network: change is "endpoint" or "path");
        await ConnectCredentialProfileAsync(sut, profile, sink, pooled);
        var changed = profile.Clone();
        switch (change)
        {
            case "endpoint": changed.ServerUrl = "https://other.example/acp"; break;
            case "path": changed.ServerUrl = "https://agent.example/other-path"; break;
            case "command": changed.StdioCommand = "different-agent"; break;
            case "arguments": changed.StdioArguments.Add("--other-account"); break;
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => ConnectCredentialProfileAsync(sut, changed, sink, pooled));

        factory.Verify(x => x.CreateChatService(It.IsAny<ServerConfiguration>()), Times.Once);
        service.Verify(x => x.DisconnectAsync(), Times.Once);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task ConnectProfile_ChangedBindingOrRevisionRecreatesSession(bool pooled, bool revisionOnly)
    {
        var firstService = CreateChatService();
        var secondService = CreateChatService();
        var factory = new Mock<IAcpChatServiceFactory>();
        factory.SetupSequence(x => x.CreateChatService(It.IsAny<ServerConfiguration>()))
            .Returns(firstService.Object).Returns(secondService.Object);
        var sut = CreateCoordinator(factory.Object, Mock.Of<ILogger<AcpChatCoordinator>>(), CreateTransportSupportPolicy(), EmptyMcpServerProvider);
        var sink = new FakeSink();
        var profile = CreateBoundProfile("unchanged-secret");
        var first = await ConnectCredentialProfileAsync(sut, profile, sink, pooled);
        var changed = profile.Clone();
        if (revisionOnly)
        {
            changed.PersistenceRevision = "new-revision";
        }
        else
        {
            changed.CredentialBinding = CredentialBindingPolicy.Create(changed,
                CredentialSource.Token, CredentialTarget.Environment, "OTHER_AGENT_TOKEN");
        }

        var second = await ConnectCredentialProfileAsync(sut, changed, sink, pooled);

        Assert.NotSame(first.ChatService, second.ChatService);
        firstService.Verify(x => x.DisconnectAsync(), Times.Once);
        factory.Verify(x => x.CreateChatService(It.IsAny<ServerConfiguration>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ConnectToProfileAsync_CapturesCredentialSnapshotBeforeFirstAwait()
    {
        var providerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseProvider = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new Mock<IAcpMcpServerProvider>();
        provider.Setup(x => x.GetMcpServersAsync(It.IsAny<CancellationToken>())).Returns(async () =>
        {
            providerEntered.TrySetResult();
            await releaseProvider.Task;
            return await EmptyMcpServerProvider.GetMcpServersAsync(TestContext.Current.CancellationToken);
        });
        var service = CreateChatService();
        var factory = new Mock<IAcpChatServiceFactory>();
        factory.Setup(x => x.CreateChatService(It.IsAny<ServerConfiguration>())).Returns(service.Object);
        var sut = CreateCoordinator(factory.Object, Mock.Of<ILogger<AcpChatCoordinator>>(), CreateTransportSupportPolicy(), provider.Object);
        var profile = CreateBoundProfile("captured-canary");
        var connect = ConnectCredentialProfileAsync(sut, profile, new FakeSink(), pooled: false);
        await providerEntered.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        try
        {
            profile.Authentication!.Token = "changed-during-await";
            profile.StdioArguments.Add("--changed");
        }
        finally
        {
            releaseProvider.TrySetResult();
        }

        await connect;

        factory.Verify(x => x.CreateChatService(It.Is<ServerConfiguration>(candidate =>
            candidate.Authentication!.Token == "captured-canary" && candidate.StdioArguments.Count == 0)), Times.Once);
    }

    [Fact]
    public async Task ConnectProfileInPoolAsync_ConcurrentChangedCredentialWaitsThenReplacesOlderSnapshot()
    {
        var initializeEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseInitialize = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstService = CreateChatService();
        firstService.Setup(x => x.InitializeAsync(It.IsAny<InitializeParams>())).Returns(async () =>
        {
            initializeEntered.TrySetResult();
            await releaseInitialize.Task;
            return new InitializeResponse(1, new AgentInfo("agent", "1.0"), new AgentCapabilities());
        });
        var secondService = CreateChatService();
        var factory = new Mock<IAcpChatServiceFactory>();
        factory.SetupSequence(x => x.CreateChatService(It.IsAny<ServerConfiguration>()))
            .Returns(firstService.Object).Returns(secondService.Object);
        var registry = new InMemoryAcpConnectionSessionRegistry();
        var sut = CreateCoordinator(factory.Object, Mock.Of<ILogger<AcpChatCoordinator>>(), CreateTransportSupportPolicy(), EmptyMcpServerProvider, sessionRegistry: registry);
        var firstProfile = CreateBoundProfile("first-canary");
        var first = ConnectCredentialProfileAsync(sut, firstProfile, new FakeSink(), pooled: true);
        await initializeEntered.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        var secondProfile = firstProfile.Clone();
        secondProfile.Authentication!.Token = "newest-canary";
        secondProfile.PersistenceRevision = "new-revision";
        var second = ConnectCredentialProfileAsync(sut, secondProfile, new FakeSink(), pooled: true);
        try
        {
            factory.Verify(x => x.CreateChatService(It.IsAny<ServerConfiguration>()), Times.Once);
        }
        finally
        {
            releaseInitialize.TrySetResult();
        }

        var results = await Task.WhenAll(first, second);

        Assert.NotSame(results[0].ChatService, results[1].ChatService);
        Assert.True(registry.TryGetByProfile(firstProfile.Id, out var session));
        Assert.Same(results[1].ChatService, session.Service);
        firstService.Verify(x => x.Dispose(), Times.Once);
    }

    [Fact]
    public async Task ConnectToProfileAsync_ClearSupersedesOlderForegroundInitialize()
    {
        var initializeEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseInitialize = new TaskCompletionSource<InitializeResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = CreateChatService();
        service.Setup(x => x.InitializeAsync(It.IsAny<InitializeParams>())).Returns(() =>
        {
            initializeEntered.TrySetResult();
            return releaseInitialize.Task;
        });
        var factory = new Mock<IAcpChatServiceFactory>();
        factory.Setup(x => x.CreateChatService(It.IsAny<ServerConfiguration>())).Returns(service.Object);
        var registry = new InMemoryAcpConnectionSessionRegistry();
        var sut = CreateCoordinator(factory.Object, Mock.Of<ILogger<AcpChatCoordinator>>(), CreateTransportSupportPolicy(), EmptyMcpServerProvider, sessionRegistry: registry);
        var sink = new FakeSink();
        var profile = CreateBoundProfile("inflight-canary");
        var older = ConnectCredentialProfileAsync(sut, profile, sink, pooled: false);
        await initializeEntered.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        var cleared = profile.Clone();
        cleared.Authentication = null;
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                ConnectCredentialProfileAsync(sut, cleared, sink, pooled: false));
        }
        finally
        {
            releaseInitialize.TrySetResult(new InitializeResponse(1, new AgentInfo("old", "1.0"), new AgentCapabilities()));
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => older);

        Assert.False(registry.TryGetByProfile(profile.Id, out _));
        Assert.Null(sink.CurrentChatService);
        service.Verify(x => x.Dispose(), Times.Once);
    }

    [Fact]
    public async Task ConnectToProfileAsync_ClearSupersedesOlderRequestWaitingForMcpProfiles()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new Mock<IAcpMcpServerProvider>();
        var calls = 0;
        provider.Setup(x => x.GetMcpServersAsync(It.IsAny<CancellationToken>())).Returns(async () =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                entered.TrySetResult();
                await release.Task;
            }

            return await EmptyMcpServerProvider.GetMcpServersAsync(TestContext.Current.CancellationToken);
        });
        var factory = new Mock<IAcpChatServiceFactory>();
        var sut = CreateCoordinator(factory.Object, Mock.Of<ILogger<AcpChatCoordinator>>(), CreateTransportSupportPolicy(), provider.Object);
        var profile = CreateBoundProfile("late-mcp-canary");
        var sink = new FakeSink();
        var older = ConnectCredentialProfileAsync(sut, profile, sink, pooled: false);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        var cleared = profile.Clone();
        cleared.Authentication = null;
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                ConnectCredentialProfileAsync(sut, cleared, sink, pooled: false));
        }
        finally
        {
            release.TrySetResult();
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => older);

        factory.Verify(x => x.CreateChatService(It.IsAny<ServerConfiguration>()), Times.Never);
        Assert.Null(sink.CurrentChatService);
    }

    [Fact]
    public async Task ConnectProfileInPoolAsync_ClearCancelsInFlightCredentialSession()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<InitializeResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = CreateChatService();
        service.Setup(x => x.InitializeAsync(It.IsAny<InitializeParams>())).Returns(() =>
        {
            entered.TrySetResult();
            return release.Task;
        });
        var factory = new Mock<IAcpChatServiceFactory>();
        factory.Setup(x => x.CreateChatService(It.IsAny<ServerConfiguration>())).Returns(service.Object);
        var registry = new InMemoryAcpConnectionSessionRegistry();
        var sut = CreateCoordinator(factory.Object, Mock.Of<ILogger<AcpChatCoordinator>>(), CreateTransportSupportPolicy(), EmptyMcpServerProvider, sessionRegistry: registry);
        var profile = CreateBoundProfile("inflight-pool-canary");
        var sink = new FakeSink();
        var older = ConnectCredentialProfileAsync(sut, profile, sink, pooled: true);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        var cleared = profile.Clone();
        cleared.Authentication = null;
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                ConnectCredentialProfileAsync(sut, cleared, sink, pooled: true));
        }
        finally
        {
            release.TrySetResult(new InitializeResponse(1, new AgentInfo("old", "1.0"), new AgentCapabilities()));
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => older);

        Assert.False(registry.TryGetByProfile(profile.Id, out _));
        service.Verify(x => x.Dispose(), Times.Once);
    }

    [Fact]
    public async Task ConnectToProfileAsync_ClearingDifferentProfileDoesNotDisconnectCurrentSession()
    {
        var service = CreateChatService();
        var factory = new Mock<IAcpChatServiceFactory>();
        factory.Setup(x => x.CreateChatService(It.IsAny<ServerConfiguration>())).Returns(service.Object);
        var sut = CreateCoordinator(factory.Object, Mock.Of<ILogger<AcpChatCoordinator>>(), CreateTransportSupportPolicy(), EmptyMcpServerProvider);
        var first = CreateBoundProfile("active-canary");
        first.Id = "active-profile";
        var sink = new FakeSink();
        var active = await ConnectCredentialProfileAsync(sut, first, sink, pooled: false);
        var cleared = CreateBoundProfile("other-canary");
        cleared.Authentication = null;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ConnectCredentialProfileAsync(sut, cleared, sink, pooled: false));

        Assert.Same(active.ChatService, sink.CurrentChatService);
        service.Verify(x => x.DisconnectAsync(), Times.Never);
    }

    private static Task<AcpTransportApplyResult> ConnectCredentialProfileAsync(
        AcpChatCoordinator coordinator, ServerConfiguration profile, FakeSink sink, bool pooled)
        => pooled
            ? coordinator.ConnectProfileInPoolAsync(profile, new FakeTransportConfiguration(), TestContext.Current.CancellationToken)
            : coordinator.ConnectToProfileAsync(profile, new FakeTransportConfiguration(), sink, TestContext.Current.CancellationToken);

    private static ServerConfiguration CreateBoundProfile(string secret, bool network = false)
    {
        var profile = new ServerConfiguration
        {
            Id = "bound-profile", Name = "Bound agent", PersistenceRevision = "original-revision",
            Transport = network ? TransportType.StreamableHttp : TransportType.Stdio,
            StdioCommand = network ? string.Empty : "agent",
            ServerUrl = network ? "https://agent.example/acp" : string.Empty,
            Authentication = new AuthenticationConfig { Token = secret }
        };
        profile.CredentialBinding = CredentialBindingPolicy.Create(profile, CredentialSource.Token,
            network ? CredentialTarget.Header : CredentialTarget.Environment,
            network ? "Authorization" : "AGENT_TOKEN", network ? "Bearer" : null);
        return profile;
    }
}
