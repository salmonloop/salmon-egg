using SalmonEgg.Domain.Services;
using SalmonEgg.Infrastructure.Services;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace SalmonEgg.Infrastructure.Tests.Services;

public sealed class LocalTerminalSessionManagerTests
{
    [Fact]
    public async Task GetOrCreateAsync_SameConversation_ReturnsExistingSessionAndCreatesOnce()
    {
        // Arrange
        var createdSessions = new List<FakeLocalTerminalSession>();
        var createCount = 0;
        var manager = CreateManager((conversationId, preferredCwd, _) =>
        {
            createCount++;
            var session = new FakeLocalTerminalSession(conversationId, preferredCwd);
            createdSessions.Add(session);
            return new ValueTask<ILocalTerminalSession>(session);
        });

        // Act
        var first = await manager.GetOrCreateAsync("conversation-1", "/workspace/one");
        var second = await manager.GetOrCreateAsync("conversation-1", "/workspace/two");

        // Assert
        var created = Assert.Single(createdSessions);
        Assert.Same(first, second);
        Assert.Same(created, first);
        Assert.Equal(1, createCount);
        Assert.Equal("/workspace/one", first.CurrentWorkingDirectory);
    }

    [Fact]
    public async Task GetOrCreateAsync_DifferentConversations_CreatesIsolatedSessions()
    {
        // Arrange
        var createdSessions = new List<FakeLocalTerminalSession>();
        var manager = CreateManager((conversationId, preferredCwd, _) =>
        {
            var session = new FakeLocalTerminalSession(conversationId, preferredCwd);
            createdSessions.Add(session);
            return new ValueTask<ILocalTerminalSession>(session);
        });

        // Act
        var first = await manager.GetOrCreateAsync("conversation-1", "/workspace/one");
        var second = await manager.GetOrCreateAsync("conversation-2", "/workspace/two");

        // Assert
        Assert.Equal(2, createdSessions.Count);
        Assert.NotSame(first, second);
        Assert.Equal("conversation-1", first.ConversationId);
        Assert.Equal("conversation-2", second.ConversationId);
        Assert.Equal("/workspace/one", first.CurrentWorkingDirectory);
        Assert.Equal("/workspace/two", second.CurrentWorkingDirectory);
    }

    [Fact]
    public async Task DisposeConversationAsync_ExistingSession_DisposesSessionAndRemovesIt()
    {
        // Arrange
        var createdSessions = new List<FakeLocalTerminalSession>();
        var manager = CreateManager((conversationId, preferredCwd, _) =>
        {
            var session = new FakeLocalTerminalSession(conversationId, preferredCwd);
            createdSessions.Add(session);
            return new ValueTask<ILocalTerminalSession>(session);
        });

        var first = await manager.GetOrCreateAsync("conversation-1", "/workspace/one");

        // Act
        await manager.DisposeConversationAsync("conversation-1");
        var second = await manager.GetOrCreateAsync("conversation-1", "/workspace/two");

        // Assert
        Assert.Equal(2, createdSessions.Count);
        Assert.Same(createdSessions[0], first);
        Assert.Equal(1, createdSessions[0].DisposeCount);
        Assert.NotSame(first, second);
        Assert.Same(createdSessions[1], second);
        Assert.Equal("/workspace/two", second.CurrentWorkingDirectory);
    }

    [Fact]
    public async Task GetOrCreateAsync_DisposedSession_CreatesReplacementSession()
    {
        // Arrange
        var createdSessions = new List<FakeLocalTerminalSession>();
        var manager = CreateManager((conversationId, preferredCwd, _) =>
        {
            var session = new FakeLocalTerminalSession(conversationId, preferredCwd);
            createdSessions.Add(session);
            return new ValueTask<ILocalTerminalSession>(session);
        });

        var first = await manager.GetOrCreateAsync("conversation-1", "/workspace/one");
        await first.DisposeAsync();

        // Act
        var second = await manager.GetOrCreateAsync("conversation-1", "/workspace/two");

        // Assert
        Assert.Equal(2, createdSessions.Count);
        Assert.Same(createdSessions[0], first);
        Assert.Equal(2, createdSessions[0].DisposeCount);
        Assert.NotSame(first, second);
        Assert.Same(createdSessions[1], second);
        Assert.Equal("/workspace/one", createdSessions[0].CurrentWorkingDirectory);
        Assert.Equal("/workspace/two", second.CurrentWorkingDirectory);
    }

    [Fact]
    public async Task GetOrCreateAsync_NonInteractiveSession_CreatesReplacementUsingLatestPreferredCwd()
    {
        // Arrange
        var createdSessions = new List<FakeLocalTerminalSession>();
        var manager = CreateManager((conversationId, preferredCwd, _) =>
        {
            var session = new FakeLocalTerminalSession(conversationId, preferredCwd);
            createdSessions.Add(session);
            return new ValueTask<ILocalTerminalSession>(session);
        });

        var first = await manager.GetOrCreateAsync("conversation-1", "/workspace/one");
        var firstFake = Assert.IsType<FakeLocalTerminalSession>(first);
        firstFake.SetCanAcceptInput(false);

        // Act
        var second = await manager.GetOrCreateAsync("conversation-1", "/workspace/two");

        // Assert
        Assert.Equal(2, createdSessions.Count);
        Assert.Same(createdSessions[0], first);
        Assert.Equal(1, createdSessions[0].DisposeCount);
        Assert.NotSame(first, second);
        Assert.Equal("/workspace/two", second.CurrentWorkingDirectory);
    }

    [Fact]
    public async Task DisposeAsync_CreatedSessions_DisposesAllSessions()
    {
        // Arrange
        var createdSessions = new List<FakeLocalTerminalSession>();
        var manager = CreateManager((conversationId, preferredCwd, _) =>
        {
            var session = new FakeLocalTerminalSession(conversationId, preferredCwd);
            createdSessions.Add(session);
            return new ValueTask<ILocalTerminalSession>(session);
        });

        await manager.GetOrCreateAsync("conversation-1", "/workspace/one");
        await manager.GetOrCreateAsync("conversation-2", "/workspace/two");

        // Act
        await manager.DisposeAsync();

        // Assert
        Assert.Equal(2, createdSessions.Count);
        Assert.All(createdSessions, session => Assert.Equal(1, session.DisposeCount));
    }

    [Fact]
    public async Task DisposeAsync_DisposedManager_RejectsFurtherOperations()
    {
        // Arrange
        var manager = CreateManager((conversationId, preferredCwd, _) =>
        {
            var session = new FakeLocalTerminalSession(conversationId, preferredCwd);
            return new ValueTask<ILocalTerminalSession>(session);
        });

        await manager.GetOrCreateAsync("conversation-1", "/workspace/one");
        await manager.DisposeAsync();

        // Act
        var getOrCreate = () => manager.GetOrCreateAsync("conversation-1", "/workspace/one").AsTask();
        var disposeConversation = () => manager.DisposeConversationAsync("conversation-1").AsTask();

        // Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(getOrCreate);
        await Assert.ThrowsAsync<ObjectDisposedException>(disposeConversation);
    }

    [Fact]
    public async Task ProcessBackedSession_WriteInputAsync_StreamsShellOutput()
    {
        // Arrange
        var output = new ConcurrentQueue<string>();
        await using var manager = new LocalTerminalSessionManager();
        var session = await manager.GetOrCreateAsync("conversation-process", Environment.CurrentDirectory);
        Assert.Equal(LocalTerminalTransportMode.PseudoConsole, session.TransportMode);
        session.OutputReceived += (_, text) => output.Enqueue(text);
        var token = "salmon-local-terminal-smoke";
        var command = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? $"echo {token}\r\n"
            : $"echo {token}\n";

        // Act
        await session.WriteInputAsync(command);
        var sawOutput = await WaitForAsync(() => string.Concat(output).Contains(token, StringComparison.Ordinal));

        // Assert
        Assert.True(sawOutput, string.Concat(output));
    }

    [Fact]
    public async Task ProcessBackedSession_WriteInputAsync_TreatsCarriageReturnAsEnter()
    {
        // Arrange
        var output = new ConcurrentQueue<string>();
        await using var manager = new LocalTerminalSessionManager();
        var session = await manager.GetOrCreateAsync("conversation-process-enter", Environment.CurrentDirectory);
        session.OutputReceived += (_, text) => output.Enqueue(text);
        var token = "salmon-local-terminal-enter";

        // Act
        await session.WriteInputAsync($"echo {token}\r");
        var sawOutput = await WaitForAsync(() => string.Concat(output).Contains(token, StringComparison.Ordinal));

        // Assert
        Assert.True(sawOutput, string.Concat(output));
    }

    [Fact]
    public async Task DisposeAsync_WhenCreationIsInFlight_DisposesCreatedSessionBeforeCompleting()
    {
        // Arrange
        var factoryEntered = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFactory = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeLocalTerminalSession? createdSession = null;
        var manager = CreateManager(async (conversationId, preferredCwd, cancellationToken) =>
        {
            factoryEntered.SetResult(null);
            await releaseFactory.Task.WaitAsync(cancellationToken);
            createdSession = new FakeLocalTerminalSession(conversationId, preferredCwd);
            return createdSession;
        });

        var getOrCreateTask = manager.GetOrCreateAsync("conversation-1", "/workspace/one").AsTask();
        await factoryEntered.Task;

        // Act
        var disposeTask = manager.DisposeAsync().AsTask();
        Assert.False(disposeTask.IsCompleted);
        releaseFactory.SetResult(null);
        var returnedSession = await getOrCreateTask;
        await disposeTask;

        // Assert
        Assert.Same(createdSession, returnedSession);
        Assert.Equal(1, createdSession!.DisposeCount);
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => manager.GetOrCreateAsync("conversation-1", "/workspace/one").AsTask());
    }

    [Theory]
    [InlineData(null, "/workspace")]
    [InlineData("", "/workspace")]
    [InlineData(" ", "/workspace")]
    [InlineData("conversation-1", null)]
    [InlineData("conversation-1", "")]
    [InlineData("conversation-1", " ")]
    public async Task GetOrCreateAsync_InvalidArguments_ThrowsArgumentException(
        string? conversationId,
        string? preferredCwd)
    {
        // Arrange
        var manager = CreateManager((id, cwd, _) =>
        {
            var session = new FakeLocalTerminalSession(id, cwd);
            return new ValueTask<ILocalTerminalSession>(session);
        });

        // Act
        Task Act() => manager.GetOrCreateAsync(conversationId!, preferredCwd!).AsTask();

        // Assert
        await Assert.ThrowsAsync<ArgumentException>(Act);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task DisposeConversationAsync_InvalidConversationId_ThrowsArgumentException(string? conversationId)
    {
        // Arrange
        var manager = CreateManager((id, cwd, _) =>
        {
            var session = new FakeLocalTerminalSession(id, cwd);
            return new ValueTask<ILocalTerminalSession>(session);
        });

        // Act
        Task Act() => manager.DisposeConversationAsync(conversationId!).AsTask();

        // Assert
        await Assert.ThrowsAsync<ArgumentException>(Act);
    }

    private static ILocalTerminalSessionManager CreateManager(
        Func<string, string, CancellationToken, ValueTask<ILocalTerminalSession>> sessionFactory)
    {
        return new LocalTerminalSessionManager(sessionFactory);
    }

    private static async Task<bool> WaitForAsync(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (predicate())
            {
                return true;
            }

            await Task.Delay(50);
        }

        return predicate();
    }

    private sealed class FakeLocalTerminalSession : ILocalTerminalSession
    {
        public FakeLocalTerminalSession(string conversationId, string currentWorkingDirectory)
        {
            ConversationId = conversationId;
            CurrentWorkingDirectory = currentWorkingDirectory;
        }

        public string ConversationId { get; }

        public string CurrentWorkingDirectory { get; }

        public LocalTerminalTransportMode TransportMode => LocalTerminalTransportMode.PseudoConsole;

        public bool CanAcceptInput { get; private set; } = true;

        public int DisposeCount { get; private set; }

        public event EventHandler<string>? OutputReceived;

        public event EventHandler? StateChanged;

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            CanAcceptInput = false;
            return ValueTask.CompletedTask;
        }

        public void SetCanAcceptInput(bool canAcceptInput)
        {
            CanAcceptInput = canAcceptInput;
        }

        public ValueTask ResizeAsync(int columns, int rows, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }

        public ValueTask WriteInputAsync(string input, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OutputReceived?.Invoke(this, input);
            StateChanged?.Invoke(this, EventArgs.Empty);
            return ValueTask.CompletedTask;
        }
    }
}
