using System.ComponentModel;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Core.Tests.Threading;
using SalmonEgg.Presentation.ViewModels.Chat;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public sealed class TerminalAuthenticationCoordinatorTests
{
    [Fact]
    public async Task Authenticate_PendingOrDeclinedConsent_DoesNotSpawn()
    {
        // Arrange
        await using var fixture = new Fixture();
        var consent = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Interaction.Confirm = (_, _) => consent.Task;

        // Act
        var pending = fixture.AuthenticateAsync();
        Assert.Equal(0, fixture.SpawnCount);
        consent.SetResult(false);

        // Assert
        Assert.False(await pending);
        Assert.Equal(0, fixture.SpawnCount);
        Assert.Equal(0, fixture.ReconnectCount);
    }

    [Fact]
    public async Task Authenticate_ExitZero_DisposesThenReconnectsWithExpectedIdentity()
    {
        // Arrange
        await using var fixture = new Fixture();
        fixture.Session.Exit.TrySetResult(0);

        // Act
        var result = await fixture.AuthenticateAsync();

        // Assert
        Assert.True(result);
        Assert.Equal(1, fixture.SpawnCount);
        Assert.Equal(1, fixture.Session.DisposeCount);
        Assert.Equal(1, fixture.ReconnectCount);
        Assert.True(fixture.ReconnectContext!.Value.ForceReconnect);
        Assert.Same(fixture.Service.Object, fixture.ReconnectContext.Value.ExpectedChatService);
        Assert.Equal("connection-1", fixture.ReconnectContext.Value.ExpectedConnectionInstanceId);
        Assert.Equal(new[] { "--acp", "login" }, fixture.Invocation!.Arguments);
        Assert.Equal("method", fixture.Invocation.Environment["TOKEN"]);
        fixture.Service.Verify(x => x.AuthenticateAsync(It.IsAny<AuthenticateParams>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    [InlineData(null)]
    public async Task Authenticate_UnsuccessfulTermination_DoesNotReconnect(int? exitCode)
    {
        // Arrange
        await using var fixture = new Fixture();
        fixture.Session.Exit.TrySetResult(exitCode);

        // Act / Assert
        Assert.False(await fixture.AuthenticateAsync());
        Assert.Equal(1, fixture.Session.DisposeCount);
        Assert.Equal(0, fixture.ReconnectCount);
    }

    [Fact]
    public async Task Authenticate_UserClosesDialog_ReclaimsSessionWithoutReconnect()
    {
        // Arrange
        await using var fixture = new Fixture();
        fixture.Interaction.Show = (_, _) => Task.CompletedTask;

        // Act / Assert
        Assert.False(await fixture.AuthenticateAsync());
        Assert.Equal(1, fixture.Session.DisposeCount);
        Assert.Equal(0, fixture.ReconnectCount);
    }

    [Fact]
    public async Task Authenticate_CallerCancels_ReclaimsSessionWithoutReconnect()
    {
        // Arrange
        await using var fixture = new Fixture();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        // Act
        var pending = fixture.AuthenticateAsync(cancellation.Token);
        await fixture.Interaction.Shown.Task.WaitAsync(TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();

        // Assert
        Assert.False(await pending);
        Assert.Equal(1, fixture.Session.DisposeCount);
        Assert.Equal(0, fixture.ReconnectCount);
    }

    [Theory]
    [InlineData("profile")]
    [InlineData("conversation")]
    [InlineData("connection")]
    [InlineData("service")]
    public async Task Authenticate_NewIntentDuringLogin_CancelsBeforeSuccessCanReconnect(string changed)
    {
        // Arrange
        await using var fixture = new Fixture();
        var pending = fixture.AuthenticateAsync();
        await fixture.Interaction.Shown.Task.WaitAsync(TestContext.Current.CancellationToken);

        // Act
        switch (changed)
        {
            case "profile": fixture.Sink.SetupGet(x => x.SelectedProfileId).Returns("profile-2"); break;
            case "conversation": fixture.Sink.SetupGet(x => x.CurrentSessionId).Returns("conversation-2"); break;
            case "connection": fixture.Sink.SetupGet(x => x.ConnectionInstanceId).Returns("connection-2"); break;
            default: fixture.Sink.SetupGet(x => x.CurrentChatService).Returns(Mock.Of<IChatService>()); break;
        }

        fixture.Sink.Raise(x => x.PropertyChanged += null, new PropertyChangedEventArgs(null));
        fixture.Session.Exit.TrySetResult(0);

        // Assert
        Assert.False(await pending);
        Assert.Equal(1, fixture.Session.DisposeCount);
        Assert.Equal(0, fixture.ReconnectCount);
    }

    [Fact]
    public async Task Authenticate_ParallelRequests_OnlyOneInteractiveSession()
    {
        // Arrange
        await using var fixture = new Fixture();
        var first = fixture.AuthenticateAsync();
        await fixture.Interaction.Shown.Task.WaitAsync(TestContext.Current.CancellationToken);

        // Act
        Assert.False(await fixture.AuthenticateAsync());
        fixture.Session.Exit.TrySetResult(0);

        // Assert
        Assert.True(await first);
        Assert.Equal(1, fixture.SpawnCount);
        Assert.Equal(1, fixture.ReconnectCount);
    }

    [Fact]
    public async Task Dispose_ActiveLogin_DrainsBeforeReturning()
    {
        // Arrange
        await using var fixture = new Fixture();
        var pending = fixture.AuthenticateAsync();
        await fixture.Interaction.Shown.Task.WaitAsync(TestContext.Current.CancellationToken);

        // Act
        await fixture.Coordinator.DisposeAsync();

        // Assert
        Assert.False(await pending);
        Assert.Equal(1, fixture.Session.DisposeCount);
    }

    [Fact]
    public async Task Authenticate_UnsupportedHost_DoesNotAskOrSpawn()
    {
        // Arrange
        await using var fixture = new Fixture();
        fixture.Factory.SetupGet(x => x.IsSupported).Returns(false);

        // Act / Assert
        Assert.False(await fixture.AuthenticateAsync());
        Assert.Equal(0, fixture.Interaction.ConfirmCount);
        Assert.Equal(0, fixture.SpawnCount);
    }

    [Fact]
    public async Task Authenticate_RemoteConnectionWithoutInvocation_DoesNotAskOrSpawn()
    {
        // Arrange
        await using var fixture = new Fixture();
        fixture.Service.As<IStdioInvocationSource>().SetupGet(x => x.StdioInvocation).Returns((StdioInvocationSnapshot?)null);

        // Act / Assert
        Assert.False(await fixture.AuthenticateAsync());
        Assert.Equal(0, fixture.Interaction.ConfirmCount);
        Assert.Equal(0, fixture.SpawnCount);
    }

    [Fact]
    public async Task Authenticate_SpawnFailureWithSecrets_DoesNotPublishExceptionText()
    {
        // Arrange
        await using var fixture = new Fixture();
        fixture.Factory.Setup(x => x.StartAsync(It.IsAny<StdioInvocationSnapshot>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("secret-command secret-env"));

        // Act / Assert
        Assert.False(await fixture.AuthenticateAsync());
        Assert.Equal(0, fixture.ReconnectCount);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public readonly Mock<IChatService> Service = new();
        public readonly Mock<IAcpChatCoordinatorSink> Sink = new();
        public readonly Mock<ITerminalAuthenticationSessionFactory> Factory = new();
        public readonly Interaction Interaction = new();
        public readonly Session Session = new();
        public readonly TerminalAuthenticationCoordinator Coordinator;
        public StdioInvocationSnapshot? Invocation;
        public AcpConnectionContext? ReconnectContext;
        public int SpawnCount;
        public int ReconnectCount;

        public Fixture()
        {
            Service.As<IStdioInvocationSource>().SetupGet(x => x.StdioInvocation).Returns(
                new StdioInvocationSnapshot("agent", ["--acp"], new Dictionary<string, string> { ["TOKEN"] = "base" }, "/work", true));
            Service.SetupGet(x => x.IsConnected).Returns(true);
            Service.SetupGet(x => x.IsInitialized).Returns(true);
            Sink.SetupGet(x => x.CurrentChatService).Returns(Service.Object);
            Sink.SetupGet(x => x.IsInitialized).Returns(true);
            Sink.SetupGet(x => x.SelectedProfileId).Returns("profile-1");
            Sink.SetupGet(x => x.CurrentSessionId).Returns("conversation-1");
            Sink.SetupGet(x => x.ConnectionInstanceId).Returns("connection-1");
            Sink.SetupGet(x => x.ConnectionGeneration).Returns(1);
            Factory.SetupGet(x => x.IsSupported).Returns(true);
            Factory.Setup(x => x.StartAsync(It.IsAny<StdioInvocationSnapshot>(), It.IsAny<CancellationToken>()))
                .Callback<StdioInvocationSnapshot, CancellationToken>((invocation, _) => { Invocation = invocation; SpawnCount++; })
                .ReturnsAsync(Session);
            Coordinator = new TerminalAuthenticationCoordinator(Factory.Object, Interaction,
                new ImmediateUiDispatcher(), NullLogger<TerminalAuthenticationCoordinator>.Instance);
        }

        public Task<bool> AuthenticateAsync(CancellationToken? cancellationToken = null)
            => Coordinator.TryAuthenticateAsync(new AuthMethodDefinition
            {
                Id = "login",
                Name = "Login",
                Type = "terminal",
                Args = ["login"],
                Env = new Dictionary<string, string> { ["TOKEN"] = "method" }
            }, Sink.Object, (context, _) =>
            {
                Assert.Equal(1, Session.DisposeCount);
                ReconnectContext = context;
                ReconnectCount++;
                return Task.FromResult(true);
            }, cancellationToken ?? TestContext.Current.CancellationToken);

        public ValueTask DisposeAsync() => Coordinator.DisposeAsync();
    }

    private sealed class Interaction : ITerminalAuthenticationInteraction
    {
        public readonly TaskCompletionSource Shown = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int ConfirmCount;
        public Func<TerminalAuthenticationViewModel, CancellationToken, Task<bool>> Confirm = (_, _) => Task.FromResult(true);
        public Func<TerminalAuthenticationViewModel, CancellationToken, Task>? Show;

        public Task<bool> ConfirmAsync(TerminalAuthenticationViewModel viewModel, CancellationToken cancellationToken)
        {
            ConfirmCount++;
            return Confirm(viewModel, cancellationToken);
        }

        public Task ShowSessionAsync(TerminalAuthenticationViewModel viewModel, CancellationToken cancellationToken)
        {
            Assert.NotNull(viewModel.Session);
            Shown.TrySetResult();
            return Show?.Invoke(viewModel, cancellationToken) ?? Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }

    private sealed class Session : ITerminalAuthenticationSession
    {
        public readonly TaskCompletionSource<int?> Exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int DisposeCount;
        public Task<int?> Completion => Exit.Task;
        public LocalTerminalTransportMode TransportMode => LocalTerminalTransportMode.PseudoConsole;
        public bool CanAcceptInput => !Completion.IsCompleted;
        public event EventHandler<string>? OutputReceived { add { } remove { } }
        public event EventHandler? StateChanged { add { } remove { } }
        public ValueTask WriteInputAsync(string input, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask ResizeAsync(int columns, int rows, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask DisposeAsync() { DisposeCount++; Exit.TrySetResult(null); return ValueTask.CompletedTask; }
    }
}
