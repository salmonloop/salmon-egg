using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Domain.Models.Content;
using SalmonEgg.Domain.Models.Plan;
using SalmonEgg.Domain.Models.Protocol;
using SalmonEgg.Domain.Models.Session;
using SalmonEgg.Domain.Models.Tool;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Core.Tests.Threading;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public sealed class AcpChatServiceAdapterTests
{
    [Fact]
    public void SessionUpdateReceived_BuffersUntilHydrated_ThenPublishes()
    {
        // Arrange
        var uiDispatcher = new ImmediateUiDispatcher();
        var inner = new FakeChatService();
        using var adapter = BuildAdapter(inner, uiDispatcher);
        var updates = new List<SessionUpdateEventArgs>();
        adapter.SessionUpdateReceived += (_, args) => updates.Add(args);

        // Act
        inner.RaiseSessionUpdate(new SessionUpdateEventArgs("remote-1", new PlanUpdate(title: "plan")));

        // Assert
        Assert.Empty(updates);

        // Act
        adapter.MarkHydrated();

        // Assert
        Assert.Single(updates);
        Assert.Equal("remote-1", updates[0].SessionId);
    }

    [Fact]
    public void SessionUpdateReceived_BufferOverflow_TriggersResyncRequired()
    {
        // Arrange
        var uiDispatcher = new ImmediateUiDispatcher();
        var inner = new FakeChatService();
        var resyncRequired = false;
        using var adapter = BuildAdapter(inner, uiDispatcher, () => resyncRequired = true, bufferLimit: 1);
        adapter.SessionUpdateReceived += (_, _) => { };

        // Act
        inner.RaiseSessionUpdate(new SessionUpdateEventArgs("remote-1", new PlanUpdate(title: "first")));
        inner.RaiseSessionUpdate(new SessionUpdateEventArgs("remote-1", new PlanUpdate(title: "second")));

        // Assert
        Assert.True(resyncRequired);
    }

    [Fact]
    public void BeginHydrationBuffering_LargeReplayDoesNotTriggerSteadyStateResyncLimit()
    {
        var uiDispatcher = new ImmediateUiDispatcher();
        var inner = new FakeChatService();
        var resyncRequired = false;
        using var adapter = BuildAdapter(inner, uiDispatcher, () => resyncRequired = true, bufferLimit: 1);
        var updates = new List<SessionUpdateEventArgs>();
        adapter.SessionUpdateReceived += (_, args) => updates.Add(args);

        var attemptId = adapter.BeginHydrationBufferingScope("remote-1");
        inner.RaiseSessionUpdate(new SessionUpdateEventArgs("remote-1", new PlanUpdate(title: "first")));
        inner.RaiseSessionUpdate(new SessionUpdateEventArgs("remote-1", new PlanUpdate(title: "second")));

        Assert.False(resyncRequired);
        Assert.Empty(updates);

        Assert.True(adapter.TryMarkHydrated(attemptId));
        Assert.Equal(2, updates.Count);
    }

    [Fact]
    public async Task ReleaseBufferedUpdatesForReplayProjectionAsync_WhenAttemptIsCurrent_PublishesTargetReplayBeforeFinalHydration()
    {
        var dispatcher = new QueueingUiDispatcher();
        var inner = new FakeChatService();
        using var adapter = BuildAdapter(inner, dispatcher);
        var updates = new List<SessionUpdateEventArgs>();
        adapter.SessionUpdateReceived += (_, args) => updates.Add(args);

        var attemptId = adapter.BeginHydrationBufferingScope("remote-1");

        var released = adapter.ReleaseBufferedUpdatesForReplayProjection(attemptId);
        inner.RaiseSessionUpdate(new SessionUpdateEventArgs("remote-1", new PlanUpdate(title: "replay")));
        while (dispatcher.RunNext())
        {
        }

        await adapter.WaitForBufferedUpdatesDrainedAsync(attemptId).WaitAsync(TimeSpan.FromSeconds(1));
        Assert.True(released);
        Assert.Single(updates);
        Assert.Equal("remote-1", updates[0].SessionId);

        inner.RaiseSessionUpdate(new SessionUpdateEventArgs("remote-2", new PlanUpdate(title: "other")));
        inner.RaiseSessionUpdate(new SessionUpdateEventArgs("remote-1", new PlanUpdate(title: "late")));
        while (dispatcher.RunNext())
        {
        }

        Assert.Equal(2, updates.Count);
        Assert.Equal("late", Assert.IsType<PlanUpdate>(updates[1].Update).Title);
        Assert.True(adapter.TryMarkHydrated(attemptId));
        while (dispatcher.RunNext())
        {
        }

        Assert.Equal(2, updates.Count);
    }

    [Fact]
    public void MarkHydrated_WithLowTrust_ReleasesBufferedUpdates()
    {
        // Arrange
        var uiDispatcher = new ImmediateUiDispatcher();
        var inner = new FakeChatService();
        using var adapter = BuildAdapter(inner, uiDispatcher);
        var updates = new List<SessionUpdateEventArgs>();
        adapter.SessionUpdateReceived += (_, args) => updates.Add(args);

        inner.RaiseSessionUpdate(new SessionUpdateEventArgs("remote-1", new PlanUpdate(title: "deferred")));
        Assert.Empty(updates);

        // Act
        adapter.MarkHydrated(lowTrust: true, reason: "GenerationAdvanced");

        // Assert
        Assert.Single(updates);
        Assert.Equal("remote-1", updates[0].SessionId);
    }

    [Fact]
    public void BeginHydrationBuffering_WhenAlreadyHydrated_BuffersOnlyTargetSessionReplay()
    {
        var uiDispatcher = new ImmediateUiDispatcher();
        var inner = new FakeChatService();
        using var adapter = BuildAdapter(inner, uiDispatcher);
        var updates = new List<SessionUpdateEventArgs>();
        adapter.SessionUpdateReceived += (_, args) => updates.Add(args);

        adapter.MarkHydrated();
        inner.RaiseSessionUpdate(new SessionUpdateEventArgs("remote-previous", new PlanUpdate(title: "before")));
        Assert.Single(updates);

        var nextAttempt = adapter.BeginHydrationBufferingScope("remote-next");
        inner.RaiseSessionUpdate(new SessionUpdateEventArgs("remote-previous", new PlanUpdate(title: "stale")));
        inner.RaiseSessionUpdate(new SessionUpdateEventArgs("remote-next", new PlanUpdate(title: "fresh")));

        Assert.Single(updates);

        adapter.MarkHydrated(nextAttempt);

        Assert.Equal(2, updates.Count);
        Assert.Equal("remote-next", updates[1].SessionId);
        Assert.Equal("fresh", Assert.IsType<PlanUpdate>(updates[1].Update).Title);
    }

    [Fact]
    public void SuppressBufferedUpdates_DropsStaleReplayUntilNextHydrationAttempt()
    {
        var uiDispatcher = new ImmediateUiDispatcher();
        var inner = new FakeChatService();
        using var adapter = BuildAdapter(inner, uiDispatcher);
        var updates = new List<SessionUpdateEventArgs>();
        adapter.SessionUpdateReceived += (_, args) => updates.Add(args);

        var firstAttempt = adapter.BeginHydrationBufferingScope("remote-1");
        inner.RaiseSessionUpdate(new SessionUpdateEventArgs("remote-1", new PlanUpdate(title: "discard-me")));

        adapter.SuppressBufferedUpdates(firstAttempt);
        adapter.MarkHydrated(firstAttempt);
        Assert.Empty(updates);

        var secondAttempt = adapter.BeginHydrationBufferingScope("remote-1");
        inner.RaiseSessionUpdate(new SessionUpdateEventArgs("remote-1", new PlanUpdate(title: "keep-me")));
        adapter.MarkHydrated(secondAttempt);

        var update = Assert.Single(updates);
        Assert.Equal("remote-1", update.SessionId);
        Assert.Equal("keep-me", Assert.IsType<PlanUpdate>(update.Update).Title);
    }

    [Fact]
    public void TryMarkHydrated_WhenSameSessionAttemptIsStale_ReturnsFalseAndDoesNotDrainCurrentAttempt()
    {
        var uiDispatcher = new ImmediateUiDispatcher();
        var inner = new FakeChatService();
        using var adapter = BuildAdapter(inner, uiDispatcher);
        var updates = new List<SessionUpdateEventArgs>();
        adapter.SessionUpdateReceived += (_, args) => updates.Add(args);

        var firstAttempt = adapter.BeginHydrationBufferingScope("remote-1");
        var secondAttempt = adapter.BeginHydrationBufferingScope("remote-1");
        inner.RaiseSessionUpdate(new SessionUpdateEventArgs("remote-1", new PlanUpdate(title: "fresh")));

        var marked = adapter.TryMarkHydrated(firstAttempt);
        Assert.False(marked);
        Assert.Empty(updates);

        Assert.True(adapter.TryMarkHydrated(secondAttempt));
        var update = Assert.Single(updates);
        Assert.Equal("remote-1", update.SessionId);
    }

    [Fact]
    public void TryMarkHydrated_WhenCompletedSameSessionAttemptIsSuperseded_ReturnsFalse()
    {
        var uiDispatcher = new ImmediateUiDispatcher();
        var inner = new FakeChatService();
        using var adapter = BuildAdapter(inner, uiDispatcher);

        var firstAttempt = adapter.BeginHydrationBufferingScope("remote-1");
        Assert.True(adapter.TryMarkHydrated(firstAttempt));

        var secondAttempt = adapter.BeginHydrationBufferingScope("remote-1");

        Assert.False(adapter.TryMarkHydrated(firstAttempt));
        Assert.True(adapter.TryMarkHydrated(secondAttempt));
    }

    [Fact]
    public void ConcurrentRemoteHydration_DoesNotDropReplayForEarlierSession()
    {
        var uiDispatcher = new ImmediateUiDispatcher();
        var inner = new FakeChatService();
        using var adapter = BuildAdapter(inner, uiDispatcher);
        var updates = new List<SessionUpdateEventArgs>();
        adapter.SessionUpdateReceived += (_, args) => updates.Add(args);

        var firstAttempt = adapter.BeginHydrationBufferingScope("remote-1");
        var secondAttempt = adapter.BeginHydrationBufferingScope("remote-2");

        inner.RaiseSessionUpdate(new SessionUpdateEventArgs(
            "remote-1",
            new AgentMessageUpdate(new TextContentBlock("first replay"))));
        inner.RaiseSessionUpdate(new SessionUpdateEventArgs(
            "remote-2",
            new AgentMessageUpdate(new TextContentBlock("second replay"))));

        Assert.True(adapter.TryMarkHydrated(firstAttempt));
        Assert.True(adapter.TryMarkHydrated(secondAttempt));

        Assert.Collection(
            updates,
            first =>
            {
                Assert.Equal("remote-1", first.SessionId);
                Assert.Equal("first replay", Assert.IsType<AgentMessageUpdate>(first.Update).Content is TextContentBlock text
                    ? text.Text
                    : null);
            },
            second =>
            {
                Assert.Equal("remote-2", second.SessionId);
                Assert.Equal("second replay", Assert.IsType<AgentMessageUpdate>(second.Update).Content is TextContentBlock text
                    ? text.Text
                    : null);
            });
    }

    [Fact]
    public void TryMarkHydrated_WhenScopedReplayIsEmpty_ReleasesSessionFilterForSteadyStateUpdates()
    {
        var uiDispatcher = new ImmediateUiDispatcher();
        var inner = new FakeChatService();
        using var adapter = BuildAdapter(inner, uiDispatcher);
        var updates = new List<SessionUpdateEventArgs>();
        adapter.SessionUpdateReceived += (_, args) => updates.Add(args);

        var attemptId = adapter.BeginHydrationBufferingScope("remote-1");

        Assert.True(adapter.TryMarkHydrated(attemptId));
        inner.RaiseSessionUpdate(new SessionUpdateEventArgs("remote-2", new PlanUpdate(title: "steady-state")));

        var update = Assert.Single(updates);
        Assert.Equal("remote-2", update.SessionId);
    }

    [Fact]
    public void SuppressBufferedUpdates_WhenLastScopedReplayIsDropped_ReleasesSessionFilterForSteadyStateUpdates()
    {
        var uiDispatcher = new ImmediateUiDispatcher();
        var inner = new FakeChatService();
        using var adapter = BuildAdapter(inner, uiDispatcher);
        var updates = new List<SessionUpdateEventArgs>();
        adapter.SessionUpdateReceived += (_, args) => updates.Add(args);

        var attemptId = adapter.BeginHydrationBufferingScope("remote-1");
        adapter.SuppressBufferedUpdates(attemptId, "test");
        inner.RaiseSessionUpdate(new SessionUpdateEventArgs("remote-2", new PlanUpdate(title: "steady-state")));

        var update = Assert.Single(updates);
        Assert.Equal("remote-2", update.SessionId);
    }

    [Fact]
    public void SuppressBufferedUpdates_WhenReplayWasReleased_RemovesScopeAndRestoresSteadyState()
    {
        var uiDispatcher = new QueueingUiDispatcher();
        var inner = new FakeChatService();
        using var adapter = BuildAdapter(inner, uiDispatcher);
        var updates = new List<SessionUpdateEventArgs>();
        adapter.SessionUpdateReceived += (_, args) => updates.Add(args);

        var attemptId = adapter.BeginHydrationBufferingScope("remote-1");
        inner.RaiseSessionUpdate(new SessionUpdateEventArgs("remote-1", new PlanUpdate(title: "released")));
        Assert.True(adapter.ReleaseBufferedUpdatesForReplayProjection(attemptId));

        adapter.SuppressBufferedUpdates(attemptId, "failed");
        inner.RaiseSessionUpdate(new SessionUpdateEventArgs("remote-2", new PlanUpdate(title: "steady-state")));
        while (uiDispatcher.RunNext())
        {
        }

        var update = Assert.Single(updates);
        Assert.Equal("remote-2", update.SessionId);
        Assert.Equal("steady-state", Assert.IsType<PlanUpdate>(update.Update).Title);
    }

    [Fact]
    public void BeginHydrationBuffering_AfterSuppressingReplacementScope_DropsCapturedStaleReplay()
    {
        var uiDispatcher = new ImmediateUiDispatcher();
        var inner = new FakeChatService();
        using var adapter = BuildAdapter(inner, uiDispatcher);
        var updates = new List<SessionUpdateEventArgs>();
        adapter.SessionUpdateReceived += (_, args) => updates.Add(args);

        _ = adapter.BeginHydrationBufferingScope("remote-1");
        var replacementAttempt = adapter.BeginHydrationBufferingScope("remote-1");
        inner.RaiseSessionUpdate(new SessionUpdateEventArgs(
            "remote-1",
            new AgentMessageUpdate(new TextContentBlock("stale"))));

        adapter.SuppressBufferedUpdates(replacementAttempt, "conflict-complete");
        var finalAttempt = adapter.BeginHydrationBufferingScope("remote-1");
        inner.RaiseSessionUpdate(new SessionUpdateEventArgs(
            "remote-1",
            new AgentMessageUpdate(new TextContentBlock("fresh"))));
        adapter.TryMarkHydrated(finalAttempt);

        var update = Assert.Single(updates);
        Assert.Equal("remote-1", update.SessionId);
        Assert.Equal("fresh", Assert.IsType<AgentMessageUpdate>(update.Update).Content is TextContentBlock text
            ? text.Text
            : null);
    }

    [Fact]
    public void MarkHydrated_WhenBufferedReplayIsLarge_YieldsBackToDispatcherBetweenDrainPasses()
    {
        var dispatcher = new QueueingUiDispatcher();
        var inner = new FakeChatService();
        using var adapter = BuildAdapter(inner, dispatcher);
        var handledCount = 0;
        var markerObservedHandledCount = -1;
        adapter.SessionUpdateReceived += (_, _) => handledCount++;

        for (var i = 0; i < 32; i++)
        {
            inner.RaiseSessionUpdate(new SessionUpdateEventArgs("remote-1", new PlanUpdate(title: $"step-{i}")));
        }

        Assert.Equal(0, handledCount);

        adapter.MarkHydrated();
        dispatcher.Enqueue(() => markerObservedHandledCount = handledCount);

        dispatcher.RunNext();
        dispatcher.RunNext();

        Assert.True(markerObservedHandledCount > 0, "Drain should begin before later UI work runs.");
        Assert.True(markerObservedHandledCount < 32, "Drain should yield before monopolizing the UI queue.");
    }

    [Fact]
    public async Task WaitForBufferedUpdatesDrainedAsync_WhenDrainStopsEarlyDueToSuppress_Completes()
    {
        var dispatcher = new QueueingUiDispatcher();
        var inner = new FakeChatService();
        using var adapter = BuildAdapter(inner, dispatcher);
        var suppressed = false;
        var attemptId = adapter.BeginHydrationBufferingScope("remote-1");
        adapter.SessionUpdateReceived += (_, _) =>
        {
            if (suppressed)
            {
                return;
            }

            suppressed = true;
            adapter.SuppressBufferedUpdates("test");
        };

        for (var i = 0; i < 16; i++)
        {
            inner.RaiseSessionUpdate(new SessionUpdateEventArgs("remote-1", new PlanUpdate(title: $"step-{i}")));
        }

        adapter.MarkHydrated(attemptId);
        var waitTask = adapter.WaitForBufferedUpdatesDrainedAsync(attemptId);

        while (dispatcher.RunNext())
        {
        }

        await waitTask.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.True(waitTask.IsCompletedSuccessfully);
    }

    private static AcpChatServiceAdapter BuildAdapter(
        IChatService inner,
        IUiDispatcher uiDispatcher,
        Action? resyncRequired = null,
        int bufferLimit = 256)
    {
        AcpChatServiceAdapter? adapter = null;
        var eventAdapter = new AcpEventAdapter(
            update => adapter!.PublishBufferedUpdate(update),
            uiDispatcher,
            bufferLimit,
            resyncRequired);
        adapter = new AcpChatServiceAdapter(inner, eventAdapter);
        return adapter;
    }

    private sealed class FakeChatService : IChatService
    {
        public string? CurrentSessionId => null;

        public bool IsInitialized => true;

        public bool IsConnected => true;

        public AgentInfo? AgentInfo => null;

        public AgentCapabilities? AgentCapabilities => null;

        public IReadOnlyList<SessionUpdateEntry> SessionHistory => Array.Empty<SessionUpdateEntry>();

        public Plan? CurrentPlan => null;

        public SessionModeState? CurrentMode => null;

        public event EventHandler<SessionUpdateEventArgs>? SessionUpdateReceived;

        public event EventHandler<PermissionRequestEventArgs>? PermissionRequestReceived
        {
            add { }
            remove { }
        }

        public event EventHandler<FileSystemRequestEventArgs>? FileSystemRequestReceived
        {
            add { }
            remove { }
        }

        public event EventHandler<TerminalRequestEventArgs>? TerminalRequestReceived
        {
            add { }
            remove { }
        }

        public event EventHandler<TerminalStateChangedEventArgs>? TerminalStateChangedReceived
        {
            add { }
            remove { }
        }

        public event EventHandler<AskUserRequestEventArgs>? AskUserRequestReceived
        {
            add { }
            remove { }
        }

        public event EventHandler<string>? ErrorOccurred
        {
            add { }
            remove { }
        }

        public void RaiseSessionUpdate(SessionUpdateEventArgs args)
        {
            SessionUpdateReceived?.Invoke(this, args);
        }

        public Task<InitializeResponse> InitializeAsync(InitializeParams @params)
            => throw new NotSupportedException();

        public Task<SessionNewResponse> CreateSessionAsync(SessionNewParams @params)
            => throw new NotSupportedException();

        public Task<SessionLoadResponse> LoadSessionAsync(SessionLoadParams @params)
            => LoadSessionAsync(@params, CancellationToken.None);

        public Task<SessionLoadResponse> LoadSessionAsync(SessionLoadParams @params, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<SessionResumeResponse> ResumeSessionAsync(SessionResumeParams @params)
            => ResumeSessionAsync(@params, CancellationToken.None);

        public Task<SessionResumeResponse> ResumeSessionAsync(SessionResumeParams @params, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<SessionCloseResponse> CloseSessionAsync(SessionCloseParams @params, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SessionListResponse> ListSessionsAsync(SessionListParams? @params = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SessionPromptResponse> SendPromptAsync(SessionPromptParams @params, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SessionSetModeResponse> SetSessionModeAsync(SessionSetModeParams @params)
            => throw new NotSupportedException();

        public Task<SessionSetConfigOptionResponse> SetSessionConfigOptionAsync(SessionSetConfigOptionParams @params)
            => throw new NotSupportedException();

        public Task<SessionCancelResponse> CancelSessionAsync(SessionCancelParams @params)
            => throw new NotSupportedException();

        public Task<AuthenticateResponse> AuthenticateAsync(AuthenticateParams @params, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<bool> RespondToPermissionRequestAsync(object messageId, string outcome, string? optionId = null)
            => throw new NotSupportedException();

        public Task<bool> RespondToFileSystemRequestAsync(object messageId, bool success, string? content = null, string? message = null)
            => throw new NotSupportedException();

        public Task<bool> RespondToAskUserRequestAsync(object messageId, IReadOnlyDictionary<string, string> answers)
            => throw new NotSupportedException();

        public Task<bool> DisconnectAsync()
            => throw new NotSupportedException();

        public Task<List<SalmonEgg.Domain.Models.Protocol.SessionMode>?> GetAvailableModesAsync()
            => throw new NotSupportedException();

        public void ClearHistory()
        {
        }
    }
}
