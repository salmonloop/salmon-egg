using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.Content;
using SalmonEgg.Domain.Models.Plan;
using SalmonEgg.Domain.Models.Protocol;
using SalmonEgg.Domain.Models.Session;
using SalmonEgg.Domain.Models.Tool;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Core.Tests.Threading;
using Uno.Extensions.Reactive;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

[Collection("NonParallel")]
public sealed class AcpConnectionCoordinatorTests
{
    [Fact]
    public async Task ResyncAsync_ReplaysBufferedUpdatesOnlyAfterSinkReset()
    {
        var uiDispatcher = new ImmediateUiDispatcher();
        var inner = new FakeChatService
        {
            AgentCapabilities = new AgentCapabilities(loadSession: true)
        };
        inner.OnLoadSessionAsync = (_, _) =>
        {
            inner.RaiseSessionUpdate(new SessionUpdateEventArgs("remote-1", new AgentMessageUpdate(new TextContentBlock("hello"))));
            return Task.FromResult(SessionLoadResponse.Completed);
        };

        AcpChatServiceAdapter? adapter = null;
        var eventAdapter = new AcpEventAdapter(
            update => adapter!.PublishBufferedUpdate(update),
            uiDispatcher);
        adapter = new AcpChatServiceAdapter(inner, eventAdapter);

        var sink = new FakeSink
        {
            CurrentChatService = adapter,
            CurrentSessionId = "conv-1",
            CurrentRemoteSessionId = "remote-1",
            IsSessionActive = true
        };

        var publishedBeforeReset = false;
        var updates = new List<SessionUpdateEventArgs>();
        adapter.SessionUpdateReceived += (_, args) =>
        {
            if (sink.ResetHydratedConversationForResyncCalls == 0)
            {
                publishedBeforeReset = true;
            }

            updates.Add(args);
        };

        var coordinator = new AcpConnectionCoordinator(
            Mock.Of<IChatConnectionStore>(),
            Mock.Of<ILogger<AcpConnectionCoordinator>>());

        await coordinator.ResyncAsync(sink);

        Assert.False(publishedBeforeReset);
        Assert.Equal(1, sink.ResetHydratedConversationForResyncCalls);
        var update = Assert.Single(updates);
        Assert.Equal("remote-1", update.SessionId);
        Assert.IsType<AgentMessageUpdate>(update.Update);
    }

    [Fact]
    public async Task ResyncAsync_LoadSessionIncludesEmptyMcpServersArray()
    {
        var inner = new FakeChatService
        {
            AgentCapabilities = new AgentCapabilities(loadSession: true)
        };
        inner.OnLoadSessionAsync = (_, _) => Task.FromResult(SessionLoadResponse.Completed);

        AcpChatServiceAdapter? adapter = null;
        var eventAdapter = new AcpEventAdapter(
            update => adapter!.PublishBufferedUpdate(update),
            new ImmediateUiDispatcher());
        adapter = new AcpChatServiceAdapter(inner, eventAdapter);

        var sink = new FakeSink
        {
            CurrentChatService = adapter,
            CurrentSessionId = "conv-1",
            CurrentRemoteSessionId = "remote-1",
            IsSessionActive = true
        };

        var coordinator = new AcpConnectionCoordinator(
            Mock.Of<IChatConnectionStore>(),
            Mock.Of<ILogger<AcpConnectionCoordinator>>());

        await coordinator.ResyncAsync(sink);

        Assert.NotNull(inner.LastLoadParams);
        Assert.Equal("remote-1", inner.LastLoadParams!.SessionId);
        Assert.NotNull(inner.LastLoadParams.McpServers);
        Assert.Empty(inner.LastLoadParams.McpServers!);
    }

    [Fact]
    public async Task ResyncAsync_WhenAdapterWasAlreadyHydrated_ReplaysOnlyAfterSinkReset()
    {
        var uiDispatcher = new ImmediateUiDispatcher();
        var inner = new FakeChatService
        {
            AgentCapabilities = new AgentCapabilities(loadSession: true)
        };

        AcpChatServiceAdapter? adapter = null;
        var eventAdapter = new AcpEventAdapter(
            update => adapter!.PublishBufferedUpdate(update),
            uiDispatcher);
        adapter = new AcpChatServiceAdapter(inner, eventAdapter);
        adapter.MarkHydrated();

        var sink = new FakeSink
        {
            CurrentChatService = adapter,
            CurrentSessionId = "conv-1",
            CurrentRemoteSessionId = "remote-1",
            IsSessionActive = true
        };

        var publishedBeforeReset = false;
        adapter.SessionUpdateReceived += (_, _) =>
        {
            if (sink.ResetHydratedConversationForResyncCalls == 0)
            {
                publishedBeforeReset = true;
            }
        };

        inner.OnLoadSessionAsync = (_, _) =>
        {
            inner.RaiseSessionUpdate(new SessionUpdateEventArgs("remote-1", new AgentMessageUpdate(new TextContentBlock("hello"))));
            return Task.FromResult(SessionLoadResponse.Completed);
        };

        var coordinator = new AcpConnectionCoordinator(
            Mock.Of<IChatConnectionStore>(),
            Mock.Of<ILogger<AcpConnectionCoordinator>>());

        await coordinator.ResyncAsync(sink);

        Assert.False(publishedBeforeReset);
    }

    [Fact]
    public async Task ResyncAsync_WhenHydrationAttemptIsStale_ClearsHydratingWithoutCompletingHydration()
    {
        var chatService = new FakeBufferedChatService
        {
            AgentCapabilities = new AgentCapabilities(loadSession: true),
            TryMarkHydratedResult = false
        };

        var sink = new FakeSink
        {
            CurrentChatService = chatService,
            CurrentSessionId = "conv-1",
            CurrentRemoteSessionId = "remote-1",
            IsSessionActive = true
        };

        var coordinator = new AcpConnectionCoordinator(
            Mock.Of<IChatConnectionStore>(),
            Mock.Of<ILogger<AcpConnectionCoordinator>>());

        await coordinator.ResyncAsync(sink);

        Assert.False(sink.IsHydrating);
        Assert.Equal(0, sink.MarkConversationRemoteHydratedCalls);
        Assert.Equal(0, chatService.WaitForDrainCalls);
    }

    [Fact]
    public async Task ResyncAsync_AppliesSessionLoadResponseBeforeCompletingHydration()
    {
        var expectedResponse = new SessionLoadResponse(
            new SessionModesState
            {
                CurrentModeId = "agent",
                AvailableModes = [new SalmonEgg.Domain.Models.Protocol.SessionMode { Id = "agent", Name = "Agent" }]
            });
        var chatService = new FakeBufferedChatService
        {
            AgentCapabilities = new AgentCapabilities(loadSession: true),
            OnLoadSessionAsync = (_, _) => Task.FromResult(expectedResponse)
        };

        var sink = new FakeSink
        {
            CurrentChatService = chatService,
            CurrentSessionId = "conv-1",
            CurrentRemoteSessionId = "remote-1",
            IsSessionActive = true
        };

        var coordinator = new AcpConnectionCoordinator(
            Mock.Of<IChatConnectionStore>(),
            Mock.Of<ILogger<AcpConnectionCoordinator>>());

        await coordinator.ResyncAsync(sink);

        Assert.Same(expectedResponse, sink.AppliedLoadResponse);
    }

    [Fact]
    public async Task ResyncAsync_WhenLoadSessionIsCanceled_ClearsHydratingAndSuppressesBufferedUpdates()
    {
        using var cts = new CancellationTokenSource();
        var chatService = new FakeBufferedChatService
        {
            AgentCapabilities = new AgentCapabilities(loadSession: true),
            OnLoadSessionAsync = (_, _) =>
            {
                cts.Cancel();
                throw new OperationCanceledException(cts.Token);
            }
        };

        var sink = new FakeSink
        {
            CurrentChatService = chatService,
            CurrentSessionId = "conv-1",
            CurrentRemoteSessionId = "remote-1",
            IsSessionActive = true
        };

        var coordinator = new AcpConnectionCoordinator(
            Mock.Of<IChatConnectionStore>(),
            Mock.Of<ILogger<AcpConnectionCoordinator>>());

        await Assert.ThrowsAsync<OperationCanceledException>(() => coordinator.ResyncAsync(sink, cts.Token));

        Assert.False(sink.IsHydrating);
        Assert.Contains(chatService.MarkHydratedCalls, call => call.LowTrust && call.Reason == "LoadSessionCanceled");
    }

    [Fact]
    public async Task ResyncAsync_WhenLoadSessionThrows_ClearsHydratingAndSuppressesBufferedUpdates()
    {
        var chatService = new FakeBufferedChatService
        {
            AgentCapabilities = new AgentCapabilities(loadSession: true),
            OnLoadSessionAsync = (_, _) => throw new InvalidOperationException("load failed")
        };

        var sink = new FakeSink
        {
            CurrentChatService = chatService,
            CurrentSessionId = "conv-1",
            CurrentRemoteSessionId = "remote-1",
            IsSessionActive = true
        };

        var coordinator = new AcpConnectionCoordinator(
            Mock.Of<IChatConnectionStore>(),
            Mock.Of<ILogger<AcpConnectionCoordinator>>());

        await coordinator.ResyncAsync(sink);

        Assert.False(sink.IsHydrating);
        Assert.Contains(chatService.MarkHydratedCalls, call => call.LowTrust && call.Reason == "LoadSessionFailed");
    }

    [Fact]
    public async Task SetConnectionInstanceIdAsync_PublishesExplicitIdentityWithoutChangingPhase()
    {
        await using var connectionState = State.Value(
            new object(),
            () => ChatConnectionState.Empty with
            {
                Phase = ConnectionPhase.Connected,
                SelectedProfileId = "profile-1",
                ConnectionInstanceId = "conn-old",
                Generation = 9
            });

        var coordinator = new AcpConnectionCoordinator(
            new ChatConnectionStore(connectionState),
            Mock.Of<ILogger<AcpConnectionCoordinator>>());

        await coordinator.SetConnectionInstanceIdAsync("conn-new");

        var updated = await connectionState ?? throw new InvalidOperationException("Connection state was not updated.");
        Assert.Equal(ConnectionPhase.Connected, updated.Phase);
        Assert.Equal("profile-1", updated.SelectedProfileId);
        Assert.Equal("conn-new", updated.ConnectionInstanceId);
        Assert.Equal(10, updated.Generation);
    }

    [Fact]
    public async Task SetDisconnectedAsync_DoesNotClearConnectionInstanceIdByItself()
    {
        await using var connectionState = State.Value(
            new object(),
            () => ChatConnectionState.Empty with
            {
                Phase = ConnectionPhase.Connected,
                SelectedProfileId = "profile-1",
                ConnectionInstanceId = "conn-1",
                Generation = 3
            });

        var coordinator = new AcpConnectionCoordinator(
            new ChatConnectionStore(connectionState),
            Mock.Of<ILogger<AcpConnectionCoordinator>>());

        await coordinator.SetDisconnectedAsync("network down");

        var updated = await connectionState ?? throw new InvalidOperationException("Connection state was not updated.");
        Assert.Equal(ConnectionPhase.Disconnected, updated.Phase);
        Assert.Equal("network down", updated.Error);
        Assert.Equal("conn-1", updated.ConnectionInstanceId);
        Assert.Equal(4, updated.Generation);
    }

    [Fact]
    public async Task ResetAsync_PreservesConnectionInstanceIdByItself()
    {
        await using var connectionState = State.Value(
            new object(),
            () => ChatConnectionState.Empty with
            {
                Phase = ConnectionPhase.Connected,
                SelectedProfileId = "profile-1",
                ConnectionInstanceId = "conn-1",
                Generation = 5
            });

        var coordinator = new AcpConnectionCoordinator(
            new ChatConnectionStore(connectionState),
            Mock.Of<ILogger<AcpConnectionCoordinator>>());

        await coordinator.ResetAsync();
        await WaitForConditionAsync(async () =>
        {
            var state = await connectionState ?? throw new InvalidOperationException("Connection state was not updated.");
            return state.Phase == ConnectionPhase.Disconnected
                && string.Equals(state.ConnectionInstanceId, "conn-1", StringComparison.Ordinal)
                && state.SelectedProfileId is null
                && state.Generation == 6;
        });

        var updated = await connectionState ?? throw new InvalidOperationException("Connection state was not updated.");
        Assert.Equal(ConnectionPhase.Disconnected, updated.Phase);
        Assert.Equal("conn-1", updated.ConnectionInstanceId);
        Assert.Null(updated.SelectedProfileId);
        Assert.Equal(6, updated.Generation);
    }

    private static async Task WaitForConditionAsync(
        Func<Task<bool>> predicate,
        int timeoutMilliseconds = 2000,
        int pollDelayMilliseconds = 10)
    {
        var timeoutAt = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
        while (DateTime.UtcNow < timeoutAt)
        {
            if (await predicate().ConfigureAwait(false))
            {
                return;
            }

            await Task.Delay(pollDelayMilliseconds).ConfigureAwait(false);
        }

        Assert.True(await predicate().ConfigureAwait(false), "Timed out waiting for expected asynchronous condition.");
    }

    private sealed class FakeSink : IAcpChatCoordinatorSink
    {
        public event PropertyChangedEventHandler? PropertyChanged
        {
            add { }
            remove { }
        }

        public IChatService? CurrentChatService { get; set; }

        public bool IsConnected { get; set; }

        public bool IsInitializing { get; set; }

        public bool IsConnecting { get; set; }

        public bool IsSessionActive { get; set; }

        public bool IsAuthenticationRequired { get; set; }

        public bool IsHydrating { get; set; }

        public string? ConnectionErrorMessage { get; set; }

        public string? AuthenticationHintMessage { get; set; }

        public string? AgentName { get; set; }

        public string? AgentVersion { get; set; }

        public string? CurrentSessionId { get; set; }

        public string? CurrentRemoteSessionId { get; set; }

        public string? SelectedProfileId { get; set; }

        public string? ConnectionInstanceId { get; set; }

        public IConversationBindingCommands ConversationBindingCommands { get; } = new NoopBindingCommands();

        public IUiDispatcher Dispatcher { get; } = new ImmediateUiDispatcher();

        public int ResetHydratedConversationForResyncCalls { get; private set; }
        public int MarkConversationRemoteHydratedCalls { get; private set; }
        public SessionLoadResponse? AppliedLoadResponse { get; private set; }

        public Task ResetHydratedConversationForResyncAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ResetHydratedConversationForResyncCalls++;
            return Task.CompletedTask;
        }

        public Task SetIsHydratingAsync(bool isHydrating, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IsHydrating = isHydrating;
            return Task.CompletedTask;
        }

        public Task MarkConversationRemoteHydratedAsync(string conversationId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(CurrentSessionId, conversationId, StringComparison.Ordinal))
            {
                MarkConversationRemoteHydratedCalls++;
            }

            return Task.CompletedTask;
        }

        public Task ApplyConversationSessionLoadResponseAsync(
            string conversationId,
            SessionLoadResponse response,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(CurrentSessionId, conversationId, StringComparison.Ordinal))
            {
                AppliedLoadResponse = response;
            }

            return Task.CompletedTask;
        }
    }

    private sealed class NoopBindingCommands : IConversationBindingCommands
    {
        public ValueTask<BindingUpdateResult> UpdateBindingAsync(string conversationId, string? remoteSessionId, string? boundProfileId)
            => ValueTask.FromResult(BindingUpdateResult.Success());
    }

    // Replaced ImmediateSynchronizationContext with ImmediateUiDispatcher from Threading/

    private class FakeChatService : IChatService
    {
        public string? CurrentSessionId => null;

        public bool IsInitialized => true;

        public bool IsConnected => true;

        public AgentInfo? AgentInfo => null;

        public AgentCapabilities? AgentCapabilities { get; set; }

        public IReadOnlyList<SessionUpdateEntry> SessionHistory => Array.Empty<SessionUpdateEntry>();

        public Plan? CurrentPlan => null;

        public SessionModeState? CurrentMode => null;

        public Func<SessionLoadParams, CancellationToken, Task<SessionLoadResponse>>? OnLoadSessionAsync { get; set; }

        public SessionLoadParams? LastLoadParams { get; private set; }

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
            => SessionUpdateReceived?.Invoke(this, args);

        public Task<InitializeResponse> InitializeAsync(InitializeParams @params)
            => throw new NotSupportedException();

        public Task<SessionNewResponse> CreateSessionAsync(SessionNewParams @params)
            => throw new NotSupportedException();

        public Task<SessionLoadResponse> LoadSessionAsync(SessionLoadParams @params)
            => LoadSessionAsync(@params, CancellationToken.None);

        public Task<SessionLoadResponse> LoadSessionAsync(SessionLoadParams @params, CancellationToken cancellationToken)
        {
            LastLoadParams = @params;
            return OnLoadSessionAsync?.Invoke(@params, cancellationToken) ?? Task.FromResult(SessionLoadResponse.Completed);
        }

        public Task<SessionListResponse> ListSessionsAsync(SessionListParams? @params = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new SessionListResponse());

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

    private sealed class FakeBufferedChatService : FakeChatService, IAcpSessionUpdateBufferController
    {
        public bool TryMarkHydratedResult { get; set; } = true;

        public int WaitForDrainCalls { get; private set; }
        public List<string> SuppressReasons { get; } = new();
        public List<(long AttemptId, bool LowTrust, string? Reason)> MarkHydratedCalls { get; } = new();

        public long BeginHydrationBufferingScope(string? sessionId) => 1;

        public void SuppressBufferedUpdates(long hydrationAttemptId, string? reason = null)
        {
            SuppressReasons.Add(reason ?? string.Empty);
        }

        public bool TryMarkHydrated(long hydrationAttemptId, bool lowTrust = false, string? reason = null)
        {
            MarkHydratedCalls.Add((hydrationAttemptId, lowTrust, reason));
            return TryMarkHydratedResult;
        }

        public Task WaitForBufferedUpdatesDrainedAsync(long hydrationAttemptId, CancellationToken cancellationToken = default)
        {
            WaitForDrainCalls++;
            return Task.CompletedTask;
        }
    }
}
