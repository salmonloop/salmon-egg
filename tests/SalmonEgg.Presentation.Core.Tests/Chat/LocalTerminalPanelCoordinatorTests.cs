using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Core.Tests.Threading;
using SalmonEgg.Presentation.ViewModels.Chat;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public class LocalTerminalPanelCoordinatorTests
{
    [Fact]
    public void LocalTerminalContracts_MustExposeInteractiveSessionCapabilities()
    {
        // Arrange
        var sessionType = typeof(ILocalTerminalSession);

        // Act
        var conversationId = sessionType.GetProperty("ConversationId");
        var currentWorkingDirectory = sessionType.GetProperty("CurrentWorkingDirectory");
        var transportMode = sessionType.GetProperty("TransportMode");
        var canAcceptInput = sessionType.GetProperty("CanAcceptInput");
        var outputReceived = sessionType.GetEvent("OutputReceived");
        var stateChanged = sessionType.GetEvent("StateChanged");
        var writeInputAsync = sessionType.GetMethod("WriteInputAsync");
        var resizeAsync = sessionType.GetMethod("ResizeAsync");
        var isAsyncDisposable = typeof(IAsyncDisposable).IsAssignableFrom(sessionType);

        // Assert
        Assert.NotNull(conversationId);
        Assert.NotNull(currentWorkingDirectory);
        Assert.NotNull(transportMode);
        Assert.NotNull(canAcceptInput);
        Assert.NotNull(outputReceived);
        Assert.Equal(typeof(EventHandler<string>), outputReceived!.EventHandlerType);
        Assert.NotNull(stateChanged);
        Assert.Equal(typeof(EventHandler), stateChanged!.EventHandlerType);
        Assert.NotNull(writeInputAsync);
        Assert.NotNull(resizeAsync);
        Assert.True(isAsyncDisposable);
    }

    [Fact]
    public void LocalTerminalManagerContract_MustSupportConversationScopedReuse()
    {
        // Arrange
        var managerType = typeof(ILocalTerminalSessionManager);

        // Act
        var getOrCreateAsync = managerType.GetMethod(
            "GetOrCreateAsync",
            new[] { typeof(string), typeof(string), typeof(CancellationToken) });
        var disposeConversationAsync = managerType.GetMethod(
            "DisposeConversationAsync",
            new[] { typeof(string), typeof(CancellationToken) });

        // Assert
        Assert.NotNull(getOrCreateAsync);
        Assert.Equal(typeof(ValueTask<ILocalTerminalSession>), getOrCreateAsync!.ReturnType);
        Assert.Equal(
            new[] { typeof(string), typeof(string), typeof(CancellationToken) },
            getOrCreateAsync.GetParameters().Select(parameter => parameter.ParameterType).ToArray());
        Assert.True(getOrCreateAsync.GetParameters()[2].HasDefaultValue);

        Assert.NotNull(disposeConversationAsync);
        Assert.Equal(typeof(ValueTask), disposeConversationAsync!.ReturnType);
        Assert.Equal(
            new[] { typeof(string), typeof(CancellationToken) },
            disposeConversationAsync.GetParameters().Select(parameter => parameter.ParameterType).ToArray());
        Assert.True(disposeConversationAsync.GetParameters()[1].HasDefaultValue);
    }

    [Fact]
    public async Task ActivateAsync_LocalConversation_UsesSessionInfoCwd()
    {
        // Arrange
        var manager = new FakeLocalTerminalSessionManager();
        var resolver = new LocalTerminalCwdResolver(() => @"C:\Users\shang");
        var coordinator = new LocalTerminalPanelCoordinator(
            manager,
            resolver,
            new ImmediateUiDispatcher());

        // Act
        var sessionViewModel = await coordinator.ActivateAsync(
            "conversation-local",
            isLocalSession: true,
            sessionInfoCwd: @"C:\repo\project");

        // Assert
        Assert.Equal(@"C:\repo\project", manager.LastRequestedCwd);
        Assert.Equal("conversation-local", sessionViewModel.ConversationId);
        Assert.Equal(@"C:\repo\project", sessionViewModel.CurrentWorkingDirectory);
        Assert.True(sessionViewModel.CanAcceptInput);
        Assert.Equal(string.Empty, sessionViewModel.OutputText);
        Assert.Same(sessionViewModel, coordinator.ActiveSession);
    }

    [Fact]
    public async Task ActivateAsync_RemoteConversation_UsesUserHome()
    {
        // Arrange
        var manager = new FakeLocalTerminalSessionManager();
        var resolver = new LocalTerminalCwdResolver(() => @"C:\Users\shang");
        var coordinator = new LocalTerminalPanelCoordinator(
            manager,
            resolver,
            new ImmediateUiDispatcher());

        // Act
        var sessionViewModel = await coordinator.ActivateAsync(
            "conversation-remote",
            isLocalSession: false,
            sessionInfoCwd: @"Z:\remote");

        // Assert
        Assert.Equal(@"C:\Users\shang", manager.LastRequestedCwd);
        Assert.Equal(@"C:\Users\shang", sessionViewModel.CurrentWorkingDirectory);
        Assert.Equal("shang", sessionViewModel.DisplayTitle);
        Assert.Same(sessionViewModel, coordinator.ActiveSession);
    }

    [Fact]
    public async Task ActivateAsync_SameConversation_ReusesSessionAndViewModel()
    {
        // Arrange
        var manager = new FakeLocalTerminalSessionManager();
        var resolver = new LocalTerminalCwdResolver(() => @"C:\Users\shang");
        var coordinator = new LocalTerminalPanelCoordinator(
            manager,
            resolver,
            new ImmediateUiDispatcher());

        // Act
        var first = await coordinator.ActivateAsync(
            "conversation-1",
            isLocalSession: true,
            sessionInfoCwd: @"C:\repo\project");
        var second = await coordinator.ActivateAsync(
            "conversation-1",
            isLocalSession: false,
            sessionInfoCwd: @"C:\ignored");

        // Assert
        Assert.Same(first, second);
        Assert.Same(first.Session, second.Session);
        Assert.Equal(1, manager.GetOrCreateCallCount);
        Assert.Equal(@"C:\repo\project", second.CurrentWorkingDirectory);
        Assert.Same(second, coordinator.ActiveSession);
    }

    [Fact]
    public async Task RemoveConversationAsync_ActiveConversation_ClearsProjectionAndCachedState()
    {
        // Arrange
        var manager = new FakeLocalTerminalSessionManager();
        var resolver = new LocalTerminalCwdResolver(() => @"C:\Users\shang");
        var coordinator = new LocalTerminalPanelCoordinator(
            manager,
            resolver,
            new ImmediateUiDispatcher());
        var first = await coordinator.ActivateAsync(
            "conversation-1",
            isLocalSession: true,
            sessionInfoCwd: @"C:\repo\project");

        // Act
        await coordinator.RemoveConversationAsync("conversation-1");
        var activeSessionAfterRemove = coordinator.ActiveSession;
        var second = await coordinator.ActivateAsync(
            "conversation-1",
            isLocalSession: true,
            sessionInfoCwd: @"C:\repo\project");

        // Assert
        Assert.Null(activeSessionAfterRemove);
        Assert.Equal(1, manager.DisposeConversationCallCount);
        Assert.Equal("conversation-1", manager.LastDisposedConversationId);
        Assert.NotSame(first, second);
        Assert.NotSame(first.Session, second.Session);
        Assert.Equal(2, manager.GetOrCreateCallCount);
    }

    [Fact]
    public async Task SessionOutputReceived_ProjectsOutputIntoViewModel()
    {
        var manager = new FakeLocalTerminalSessionManager();
        var coordinator = new LocalTerminalPanelCoordinator(
            manager,
            new LocalTerminalCwdResolver(() => @"C:\Users\shang"),
            new ImmediateUiDispatcher());

        var sessionViewModel = await coordinator.ActivateAsync(
            "conversation-output",
            isLocalSession: true,
            sessionInfoCwd: @"C:\repo\project");

        await sessionViewModel.Session.WriteInputAsync("hello");

        Assert.Equal("hello", sessionViewModel.OutputText);
    }

    [Fact]
    public async Task SessionStateChanged_RefreshesProjectedMetadata()
    {
        var manager = new FakeLocalTerminalSessionManager();
        var coordinator = new LocalTerminalPanelCoordinator(
            manager,
            new LocalTerminalCwdResolver(() => @"C:\Users\shang"),
            new ImmediateUiDispatcher());

        var sessionViewModel = await coordinator.ActivateAsync(
            "conversation-state",
            isLocalSession: true,
            sessionInfoCwd: @"C:\repo\project");
        var fakeSession = Assert.IsType<FakeLocalTerminalSession>(sessionViewModel.Session);

        fakeSession.UpdateState(@"C:\repo\changed", canAcceptInput: false);
        await fakeSession.ResizeAsync(120, 40);

        Assert.Equal(@"C:\repo\changed", sessionViewModel.CurrentWorkingDirectory);
        Assert.Equal("changed", sessionViewModel.DisplayTitle);
        Assert.False(sessionViewModel.CanAcceptInput);
    }

    [Fact]
    public async Task RemoveConversationAsync_DetachesSessionEvents()
    {
        var manager = new FakeLocalTerminalSessionManager();
        var coordinator = new LocalTerminalPanelCoordinator(
            manager,
            new LocalTerminalCwdResolver(() => @"C:\Users\shang"),
            new ImmediateUiDispatcher());

        var sessionViewModel = await coordinator.ActivateAsync(
            "conversation-detach",
            isLocalSession: true,
            sessionInfoCwd: @"C:\repo\project");
        var fakeSession = Assert.IsType<FakeLocalTerminalSession>(sessionViewModel.Session);

        await coordinator.RemoveConversationAsync("conversation-detach");
        fakeSession.RaiseOutput("after-remove");
        fakeSession.UpdateState(@"C:\repo\after", canAcceptInput: false);
        await fakeSession.ResizeAsync(120, 40);

        Assert.Equal(string.Empty, sessionViewModel.OutputText);
        Assert.Equal(@"C:\repo\project", sessionViewModel.CurrentWorkingDirectory);
        Assert.Equal("project", sessionViewModel.DisplayTitle);
        Assert.True(sessionViewModel.CanAcceptInput);
    }

    [Fact]
    public async Task DisposeAsync_DetachesSessionEventsAndClearsActiveSession()
    {
        var manager = new FakeLocalTerminalSessionManager();
        await using var coordinator = new LocalTerminalPanelCoordinator(
            manager,
            new LocalTerminalCwdResolver(() => @"C:\Users\shang"),
            new ImmediateUiDispatcher());

        var sessionViewModel = await coordinator.ActivateAsync(
            "conversation-dispose",
            isLocalSession: true,
            sessionInfoCwd: @"C:\repo\project");
        var fakeSession = Assert.IsType<FakeLocalTerminalSession>(sessionViewModel.Session);

        await coordinator.DisposeAsync();
        fakeSession.RaiseOutput("after-dispose");
        fakeSession.UpdateState(@"C:\repo\after-dispose", canAcceptInput: false);
        await fakeSession.ResizeAsync(120, 40);

        Assert.Null(coordinator.ActiveSession);
        Assert.Equal(1, manager.DisposeManagerCallCount);
        Assert.Equal(string.Empty, sessionViewModel.OutputText);
        Assert.Equal(@"C:\repo\project", sessionViewModel.CurrentWorkingDirectory);
        Assert.Equal("project", sessionViewModel.DisplayTitle);
        Assert.True(sessionViewModel.CanAcceptInput);
    }

    [Fact]
    public async Task QueuedOutputAfterRemove_DoesNotMutateDetachedViewModel()
    {
        var manager = new FakeLocalTerminalSessionManager();
        var dispatcher = new EventQueueingUiDispatcher();
        var coordinator = new LocalTerminalPanelCoordinator(
            manager,
            new LocalTerminalCwdResolver(() => @"C:\Users\shang"),
            dispatcher);

        var activation = coordinator.ActivateAsync(
            "conversation-queued",
            isLocalSession: true,
            sessionInfoCwd: @"C:\repo\project");
        dispatcher.RunAll();
        var sessionViewModel = await activation;
        var fakeSession = Assert.IsType<FakeLocalTerminalSession>(sessionViewModel.Session);

        fakeSession.RaiseOutput("queued");
        Assert.Equal(1, dispatcher.PendingCount);

        var remove = coordinator.RemoveConversationAsync("conversation-queued");
        dispatcher.RunAll();
        await remove;
        dispatcher.RunAll();

        Assert.Equal(string.Empty, sessionViewModel.OutputText);
    }

    private sealed class FakeLocalTerminalSessionManager : ILocalTerminalSessionManager
    {
        private readonly Dictionary<string, FakeLocalTerminalSession> _sessions = new(StringComparer.Ordinal);

        public int GetOrCreateCallCount { get; private set; }

        public int DisposeConversationCallCount { get; private set; }

        public int DisposeManagerCallCount { get; private set; }

        public string? LastRequestedConversationId { get; private set; }

        public string? LastRequestedCwd { get; private set; }

        public string? LastDisposedConversationId { get; private set; }

        public ValueTask<ILocalTerminalSession> GetOrCreateAsync(
            string conversationId,
            string preferredCwd,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GetOrCreateCallCount++;
            LastRequestedConversationId = conversationId;
            LastRequestedCwd = preferredCwd;

            if (!_sessions.TryGetValue(conversationId, out var session))
            {
                session = new FakeLocalTerminalSession(conversationId, preferredCwd);
                _sessions.Add(conversationId, session);
            }

            return ValueTask.FromResult<ILocalTerminalSession>(session);
        }

        public ValueTask DisposeConversationAsync(
            string conversationId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DisposeConversationCallCount++;
            LastDisposedConversationId = conversationId;
            _sessions.Remove(conversationId);
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            DisposeManagerCallCount++;
            _sessions.Clear();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeLocalTerminalSession : ILocalTerminalSession
    {
        public FakeLocalTerminalSession(string conversationId, string currentWorkingDirectory)
        {
            ConversationId = conversationId;
            CurrentWorkingDirectory = currentWorkingDirectory;
            CanAcceptInput = true;
        }

        public string ConversationId { get; }

        public string CurrentWorkingDirectory { get; private set; }

        public LocalTerminalTransportMode TransportMode => LocalTerminalTransportMode.PseudoConsole;

        public bool CanAcceptInput { get; private set; }

        public event EventHandler<string>? OutputReceived;

        public event EventHandler? StateChanged;

        public ValueTask WriteInputAsync(string input, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OutputReceived?.Invoke(this, input);
            return ValueTask.CompletedTask;
        }

        public ValueTask ResizeAsync(int columns, int rows, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StateChanged?.Invoke(this, EventArgs.Empty);
            return ValueTask.CompletedTask;
        }

        public void RaiseOutput(string output)
        {
            OutputReceived?.Invoke(this, output);
        }

        public void UpdateState(string currentWorkingDirectory, bool canAcceptInput)
        {
            CurrentWorkingDirectory = currentWorkingDirectory;
            CanAcceptInput = canAcceptInput;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class EventQueueingUiDispatcher : IUiDispatcher
    {
        private readonly Queue<Action> _callbacks = new();

        public bool HasThreadAccess => false;

        public int PendingCount => _callbacks.Count;

        public void Enqueue(Action action)
        {
            _callbacks.Enqueue(action);
        }

        public Task EnqueueAsync(Action action)
        {
            action();
            return Task.CompletedTask;
        }

        public Task EnqueueAsync(Func<Task> function)
        {
            return function();
        }

        public void RunAll()
        {
            while (_callbacks.Count > 0)
            {
                _callbacks.Dequeue()();
            }
        }
    }
}
