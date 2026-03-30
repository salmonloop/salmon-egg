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
using SalmonEgg.Presentation.Core.Services.Chat;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public sealed class AcpChatServiceAdapterTests
{
    [Fact]
    public void SessionUpdateReceived_BuffersUntilHydrated_ThenPublishes()
    {
        // Arrange
        var syncContext = new ImmediateSynchronizationContext();
        var inner = new FakeChatService();
        using var adapter = BuildAdapter(inner, syncContext);
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
        var syncContext = new ImmediateSynchronizationContext();
        var inner = new FakeChatService();
        var resyncRequired = false;
        using var adapter = BuildAdapter(inner, syncContext, () => resyncRequired = true, bufferLimit: 1);
        adapter.SessionUpdateReceived += (_, _) => { };

        // Act
        inner.RaiseSessionUpdate(new SessionUpdateEventArgs("remote-1", new PlanUpdate(title: "first")));
        inner.RaiseSessionUpdate(new SessionUpdateEventArgs("remote-1", new PlanUpdate(title: "second")));

        // Assert
        Assert.True(resyncRequired);
    }

    [Fact]
    public void MarkHydrated_WithLowTrust_ReleasesBufferedUpdates()
    {
        // Arrange
        var syncContext = new ImmediateSynchronizationContext();
        var inner = new FakeChatService();
        using var adapter = BuildAdapter(inner, syncContext);
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
        var syncContext = new ImmediateSynchronizationContext();
        var inner = new FakeChatService();
        using var adapter = BuildAdapter(inner, syncContext);
        var updates = new List<SessionUpdateEventArgs>();
        adapter.SessionUpdateReceived += (_, args) => updates.Add(args);

        adapter.MarkHydrated();
        inner.RaiseSessionUpdate(new SessionUpdateEventArgs("remote-previous", new PlanUpdate(title: "before")));
        Assert.Single(updates);

        adapter.BeginHydrationBuffering("remote-next");
        inner.RaiseSessionUpdate(new SessionUpdateEventArgs("remote-previous", new PlanUpdate(title: "stale")));
        inner.RaiseSessionUpdate(new SessionUpdateEventArgs("remote-next", new PlanUpdate(title: "fresh")));

        Assert.Single(updates);

        adapter.MarkHydrated();

        Assert.Equal(2, updates.Count);
        Assert.Equal("remote-next", updates[1].SessionId);
        Assert.Equal("fresh", Assert.IsType<PlanUpdate>(updates[1].Update).Title);
    }

    [Fact]
    public void SuppressBufferedUpdates_DropsStaleReplayUntilNextHydrationAttempt()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var inner = new FakeChatService();
        using var adapter = BuildAdapter(inner, syncContext);
        var updates = new List<SessionUpdateEventArgs>();
        adapter.SessionUpdateReceived += (_, args) => updates.Add(args);

        adapter.BeginHydrationBuffering("remote-1");
        inner.RaiseSessionUpdate(new SessionUpdateEventArgs("remote-1", new PlanUpdate(title: "discard-me")));

        adapter.SuppressBufferedUpdates();
        adapter.MarkHydrated();
        Assert.Empty(updates);

        adapter.BeginHydrationBuffering("remote-1");
        inner.RaiseSessionUpdate(new SessionUpdateEventArgs("remote-1", new PlanUpdate(title: "keep-me")));
        adapter.MarkHydrated();

        var update = Assert.Single(updates);
        Assert.Equal("remote-1", update.SessionId);
        Assert.Equal("keep-me", Assert.IsType<PlanUpdate>(update.Update).Title);
    }

    private static AcpChatServiceAdapter BuildAdapter(
        IChatService inner,
        SynchronizationContext synchronizationContext,
        Action? resyncRequired = null,
        int bufferLimit = 256)
    {
        AcpChatServiceAdapter? adapter = null;
        var eventAdapter = new AcpEventAdapter(
            update => adapter!.PublishBufferedUpdate(update),
            synchronizationContext,
            bufferLimit,
            resyncRequired);
        adapter = new AcpChatServiceAdapter(inner, eventAdapter);
        return adapter;
    }

    private sealed class ImmediateSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state)
        {
            d(state);
        }
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
