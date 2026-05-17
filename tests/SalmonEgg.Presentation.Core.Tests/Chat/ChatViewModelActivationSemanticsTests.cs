using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Domain.Models.Protocol;
using SalmonEgg.Domain.Models.Session;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Services.Chat;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public partial class ChatViewModelTests
{
    [Fact]
    public async Task SwitchConversationAsync_WhenRemoteHydratingTargetBecomesWarmDuringSelection_SkipsRemoteSessionLoad()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var sessions = new Dictionary<string, Session>(StringComparer.Ordinal);
        var sessionManager = new Mock<ISessionManager>();
        sessionManager.Setup(s => s.GetSession(It.IsAny<string>()))
            .Returns<string>(id => sessions.TryGetValue(id, out var session) ? session : null);
        sessionManager.Setup(s => s.CreateSessionAsync(It.IsAny<string>(), It.IsAny<string?>()))
            .Returns<string, string?>((id, cwd) =>
            {
                var session = new Session(id, cwd);
                sessions[id] = session;
                return Task.FromResult(session);
            });
        sessionManager.Setup(s => s.UpdateSession(It.IsAny<string>(), It.IsAny<Action<Session>>(), It.IsAny<bool>()))
            .Returns<string, Action<Session>, bool>((id, update, updateActivity) =>
            {
                if (!sessions.TryGetValue(id, out var session))
                {
                    return false;
                }

                update(session);
                if (updateActivity)
                {
                    session.UpdateActivity();
                }

                return true;
            });
        sessionManager.Setup(s => s.RemoveSession(It.IsAny<string>()))
            .Returns<string>(id => sessions.Remove(id));

        await sessionManager.Object.CreateSessionAsync("conv-local", @"C:\repo\local");
        await sessionManager.Object.CreateSessionAsync("conv-remote", @"C:\repo\remote");

        ViewModelFixture? fixture = null;
        var activationCoordinator = new Mock<IConversationActivationCoordinator>();
        activationCoordinator
            .Setup(coordinator => coordinator.ActivateSessionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<string, CancellationToken>((sessionId, cancellationToken) =>
                MarkTargetWarmDuringSelectionAsync(sessionId, cancellationToken));
        activationCoordinator
            .Setup(coordinator => coordinator.ActivateSessionAsync(
                It.IsAny<string>(),
                It.IsAny<ConversationActivationHydrationMode>(),
                It.IsAny<CancellationToken>()))
            .Returns<string, ConversationActivationHydrationMode, CancellationToken>((sessionId, _, cancellationToken) =>
                MarkTargetWarmDuringSelectionAsync(sessionId, cancellationToken));

        var remoteLoadCount = 0;
        var chatService = CreateConnectedChatService();
        chatService.SetupGet(service => service.AgentCapabilities).Returns(new AgentCapabilities(loadSession: true));
        chatService.Setup(service => service.LoadSessionAsync(
                It.Is<SessionLoadParams>(parameters =>
                    string.Equals(parameters.SessionId, "remote-1", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()))
            .Returns<SessionLoadParams, CancellationToken>((_, _) =>
            {
                Interlocked.Increment(ref remoteLoadCount);
                return Task.FromResult(SessionLoadResponse.Completed);
            });

        fixture = CreateViewModel(
            syncContext,
            sessionManager: sessionManager,
            conversationActivationCoordinator: activationCoordinator.Object);
        await using (fixture)
        {
            await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.RestoreAsync());

            fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
                ConversationId: "conv-local",
                Transcript:
                [
                    new ConversationMessageSnapshot
                    {
                        Id = "local-1",
                        Timestamp = new DateTime(2026, 5, 3, 0, 0, 0, DateTimeKind.Utc),
                        IsOutgoing = true,
                        ContentType = "text",
                        TextContent = "local cached transcript"
                    }
                ],
                Plan: [],
                ShowPlanPanel: false,
                CreatedAt: new DateTime(2026, 5, 3, 0, 0, 0, DateTimeKind.Utc),
                LastUpdatedAt: new DateTime(2026, 5, 3, 0, 0, 0, DateTimeKind.Utc)));
            fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
                ConversationId: "conv-remote",
                Transcript:
                [
                    new ConversationMessageSnapshot
                    {
                        Id = "remote-1",
                        Timestamp = new DateTime(2026, 5, 3, 0, 0, 1, DateTimeKind.Utc),
                        IsOutgoing = false,
                        ContentType = "text",
                        TextContent = "remote cached transcript"
                    }
                ],
                Plan: [],
                ShowPlanPanel: false,
                CreatedAt: new DateTime(2026, 5, 3, 0, 0, 0, DateTimeKind.Utc),
                LastUpdatedAt: new DateTime(2026, 5, 3, 0, 0, 1, DateTimeKind.Utc),
                ConnectionInstanceId: "conn-1"),
                ConversationWorkspaceSnapshotOrigin.RuntimeProjection);

            await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.ReplaceChatServiceAsync(chatService.Object));
            await fixture.UpdateStateAsync(state => state with
            {
                HydratedConversationId = "conv-local",
                Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                    .Add("conv-remote", new ConversationBindingSlice("conv-remote", "remote-1", "profile-1")),
                ConversationContents = ImmutableDictionary<string, ConversationContentSlice>.Empty
                    .Add("conv-remote", new ConversationContentSlice(
                        ImmutableList.Create(
                            new ConversationMessageSnapshot
                            {
                                Id = "remote-1",
                                Timestamp = new DateTime(2026, 5, 3, 0, 0, 1, DateTimeKind.Utc),
                                IsOutgoing = false,
                                ContentType = "text",
                                TextContent = "remote cached transcript"
                            }),
                        ImmutableList<ConversationPlanEntrySnapshot>.Empty,
                        false)),
                RuntimeStates = ImmutableDictionary<string, ConversationRuntimeSlice>.Empty
                    .Add("conv-remote", new ConversationRuntimeSlice(
                        ConversationId: "conv-remote",
                        Phase: ConversationRuntimePhase.RemoteHydrating,
                        ConnectionInstanceId: "conn-1",
                        RemoteSessionId: "remote-1",
                        ProfileId: "profile-1",
                        Reason: "RemoteHydrationPending",
                        UpdatedAtUtc: new DateTime(2026, 5, 3, 0, 0, 2, DateTimeKind.Utc)))
            });
            await DispatchConnectedAsync(fixture, "profile-1");
            await fixture.DispatchConnectionAsync(new SetConnectionInstanceIdAction("conn-1"));
            await WaitForConditionAsync(() =>
                Task.FromResult(string.Equals(fixture.ViewModel.ConnectionInstanceId, "conn-1", StringComparison.Ordinal)));

            var switchedRemote = await fixture.ViewModel.SwitchConversationAsync("conv-remote");

            Assert.True(switchedRemote);
            await WaitForConditionAsync(() => Task.FromResult(
                string.Equals(fixture.ViewModel.CurrentSessionId, "conv-remote", StringComparison.Ordinal)),
                timeoutMilliseconds: 15000);
            Assert.True(Volatile.Read(ref remoteLoadCount) <= 1);
            var finalState = await fixture.GetStateAsync();
            var finalRuntime = finalState.ResolveRuntimeState("conv-remote");
            Assert.NotNull(finalRuntime);
            Assert.Equal(ConversationRuntimePhase.Warm, finalRuntime!.Value.Phase);
            Assert.True(
                string.Equals(finalRuntime.Value.Reason, ConversationRuntimeReasons.WarmReuse, StringComparison.Ordinal)
                || string.Equals(finalRuntime.Value.Reason, ConversationRuntimeReasons.SessionLoadCompleted, StringComparison.Ordinal),
                finalRuntime.Value.Reason);
        }

        async Task<ConversationActivationResult> MarkTargetWarmDuringSelectionAsync(
            string sessionId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(sessionId, "conv-remote", StringComparison.Ordinal))
            {
                await fixture!.UpdateStateAsync(state => state with
                {
                    RuntimeStates = (state.RuntimeStates ?? ImmutableDictionary<string, ConversationRuntimeSlice>.Empty).SetItem(
                        "conv-remote",
                        new ConversationRuntimeSlice(
                            ConversationId: "conv-remote",
                            Phase: ConversationRuntimePhase.Warm,
                            ConnectionInstanceId: "conn-1",
                            RemoteSessionId: "remote-1",
                            ProfileId: "profile-1",
                            Reason: "WarmReuse",
                            UpdatedAtUtc: new DateTime(2026, 5, 3, 0, 0, 3, DateTimeKind.Utc)))
                });
            }

            return new ConversationActivationResult(true, sessionId, null);
        }
    }

    [Fact]
    public async Task SwitchConversationAsync_WhenStartComposerIntentDiffersFromWarmRemoteBinding_StillSkipsRemoteSessionLoad()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var sessions = new Dictionary<string, Session>(StringComparer.Ordinal);
        var sessionManager = new Mock<ISessionManager>();
        sessionManager.Setup(s => s.GetSession(It.IsAny<string>()))
            .Returns<string>(id => sessions.TryGetValue(id, out var session) ? session : null);
        sessionManager.Setup(s => s.CreateSessionAsync(It.IsAny<string>(), It.IsAny<string?>()))
            .Returns<string, string?>((id, cwd) =>
            {
                var session = new Session(id, cwd);
                sessions[id] = session;
                return Task.FromResult(session);
            });
        sessionManager.Setup(s => s.UpdateSession(It.IsAny<string>(), It.IsAny<Action<Session>>(), It.IsAny<bool>()))
            .Returns<string, Action<Session>, bool>((id, update, updateActivity) =>
            {
                if (!sessions.TryGetValue(id, out var session))
                {
                    return false;
                }

                update(session);
                if (updateActivity)
                {
                    session.UpdateActivity();
                }

                return true;
            });
        sessionManager.Setup(s => s.RemoveSession(It.IsAny<string>()))
            .Returns<string>(id => sessions.Remove(id));

        await sessionManager.Object.CreateSessionAsync("conv-local", @"C:\repo\local");
        await sessionManager.Object.CreateSessionAsync("conv-remote", @"C:\repo\remote");

        ViewModelFixture? fixture = null;
        var activationCoordinator = new Mock<IConversationActivationCoordinator>();
        activationCoordinator
            .Setup(coordinator => coordinator.ActivateSessionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<string, CancellationToken>((sessionId, cancellationToken) =>
                MarkTargetWarmDuringSelectionAsync(sessionId, cancellationToken));
        activationCoordinator
            .Setup(coordinator => coordinator.ActivateSessionAsync(
                It.IsAny<string>(),
                It.IsAny<ConversationActivationHydrationMode>(),
                It.IsAny<CancellationToken>()))
            .Returns<string, ConversationActivationHydrationMode, CancellationToken>((sessionId, _, cancellationToken) =>
                MarkTargetWarmDuringSelectionAsync(sessionId, cancellationToken));

        var remoteLoadCount = 0;
        var chatService = CreateConnectedChatService();
        chatService.SetupGet(service => service.AgentCapabilities).Returns(new AgentCapabilities(loadSession: true));
        chatService.Setup(service => service.LoadSessionAsync(
                It.Is<SessionLoadParams>(parameters =>
                    string.Equals(parameters.SessionId, "remote-1", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()))
            .Returns<SessionLoadParams, CancellationToken>((_, _) =>
            {
                Interlocked.Increment(ref remoteLoadCount);
                return Task.FromResult(SessionLoadResponse.Completed);
            });

        fixture = CreateViewModel(
            syncContext,
            sessionManager: sessionManager,
            conversationActivationCoordinator: activationCoordinator.Object);
        await using (fixture)
        {
            await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.RestoreAsync());

            fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
                ConversationId: "conv-local",
                Transcript:
                [
                    new ConversationMessageSnapshot
                    {
                        Id = "local-1",
                        Timestamp = new DateTime(2026, 5, 3, 0, 0, 0, DateTimeKind.Utc),
                        IsOutgoing = true,
                        ContentType = "text",
                        TextContent = "local cached transcript"
                    }
                ],
                Plan: [],
                ShowPlanPanel: false,
                CreatedAt: new DateTime(2026, 5, 3, 0, 0, 0, DateTimeKind.Utc),
                LastUpdatedAt: new DateTime(2026, 5, 3, 0, 0, 0, DateTimeKind.Utc)));
            fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
                ConversationId: "conv-remote",
                Transcript:
                [
                    new ConversationMessageSnapshot
                    {
                        Id = "remote-1",
                        Timestamp = new DateTime(2026, 5, 3, 0, 0, 1, DateTimeKind.Utc),
                        IsOutgoing = false,
                        ContentType = "text",
                        TextContent = "remote cached transcript"
                    }
                ],
                Plan: [],
                ShowPlanPanel: false,
                CreatedAt: new DateTime(2026, 5, 3, 0, 0, 0, DateTimeKind.Utc),
                LastUpdatedAt: new DateTime(2026, 5, 3, 0, 0, 1, DateTimeKind.Utc),
                ConnectionInstanceId: "conn-1"),
                ConversationWorkspaceSnapshotOrigin.RuntimeProjection);

            await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.ReplaceChatServiceAsync(chatService.Object));
            await fixture.UpdateStateAsync(state => state with
            {
                HydratedConversationId = "conv-local",
                Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                    .Add("conv-remote", new ConversationBindingSlice("conv-remote", "remote-1", "profile-1")),
                ConversationContents = ImmutableDictionary<string, ConversationContentSlice>.Empty
                    .Add("conv-remote", new ConversationContentSlice(
                        ImmutableList.Create(
                            new ConversationMessageSnapshot
                            {
                                Id = "remote-1",
                                Timestamp = new DateTime(2026, 5, 3, 0, 0, 1, DateTimeKind.Utc),
                                IsOutgoing = false,
                                ContentType = "text",
                                TextContent = "remote cached transcript"
                            }),
                        ImmutableList<ConversationPlanEntrySnapshot>.Empty,
                        false)),
                RuntimeStates = ImmutableDictionary<string, ConversationRuntimeSlice>.Empty
                    .Add("conv-remote", new ConversationRuntimeSlice(
                        ConversationId: "conv-remote",
                        Phase: ConversationRuntimePhase.RemoteHydrating,
                        ConnectionInstanceId: "conn-1",
                        RemoteSessionId: "remote-1",
                        ProfileId: "profile-1",
                        Reason: "RemoteHydrationPending",
                        UpdatedAtUtc: new DateTime(2026, 5, 3, 0, 0, 2, DateTimeKind.Utc)))
            });

            fixture.ViewModel.CurrentPrompt = "start composer draft";
            await fixture.DispatchConnectionAsync(new SetSettingsSelectedProfileAction("profile-2"));
            await DispatchConnectedAsync(fixture, "profile-1");
            await fixture.DispatchConnectionAsync(new SetConnectionInstanceIdAction("conn-1"));
            await WaitForConditionAsync(() =>
                Task.FromResult(string.Equals(fixture.ViewModel.ConnectionInstanceId, "conn-1", StringComparison.Ordinal)));

            var switchedRemote = await fixture.ViewModel.SwitchConversationAsync("conv-remote");

            Assert.True(switchedRemote);
            await WaitForConditionAsync(() => Task.FromResult(
                string.Equals(fixture.ViewModel.CurrentSessionId, "conv-remote", StringComparison.Ordinal)),
                timeoutMilliseconds: 15000);
            Assert.True(Volatile.Read(ref remoteLoadCount) <= 1);
            var finalState = await fixture.GetStateAsync();
            var finalRuntime = finalState.ResolveRuntimeState("conv-remote");
            Assert.NotNull(finalRuntime);
            Assert.Equal(ConversationRuntimePhase.Warm, finalRuntime!.Value.Phase);
            Assert.True(
                string.Equals(finalRuntime.Value.Reason, ConversationRuntimeReasons.WarmReuse, StringComparison.Ordinal)
                || string.Equals(finalRuntime.Value.Reason, ConversationRuntimeReasons.SessionLoadCompleted, StringComparison.Ordinal),
                finalRuntime.Value.Reason);
        }

        async Task<ConversationActivationResult> MarkTargetWarmDuringSelectionAsync(
            string sessionId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(sessionId, "conv-remote", StringComparison.Ordinal))
            {
                await fixture!.UpdateStateAsync(state => state with
                {
                    RuntimeStates = (state.RuntimeStates ?? ImmutableDictionary<string, ConversationRuntimeSlice>.Empty).SetItem(
                        "conv-remote",
                        new ConversationRuntimeSlice(
                            ConversationId: "conv-remote",
                            Phase: ConversationRuntimePhase.Warm,
                            ConnectionInstanceId: "conn-1",
                            RemoteSessionId: "remote-1",
                            ProfileId: "profile-1",
                            Reason: "WarmReuse",
                            UpdatedAtUtc: new DateTime(2026, 5, 3, 0, 0, 3, DateTimeKind.Utc)))
                });
            }

            return new ConversationActivationResult(true, sessionId, null);
        }
    }

    [Fact]
    public async Task SwitchConversationAsync_WhenCompetingActivationExistsAndTargetBecomesWarmAfterSelection_SkipsRemoteSessionLoad()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var sessions = new Dictionary<string, Session>(StringComparer.Ordinal);
        var sessionManager = new Mock<ISessionManager>();
        sessionManager.Setup(s => s.GetSession(It.IsAny<string>()))
            .Returns<string>(id => sessions.TryGetValue(id, out var session) ? session : null);
        sessionManager.Setup(s => s.CreateSessionAsync(It.IsAny<string>(), It.IsAny<string?>()))
            .Returns<string, string?>((id, cwd) =>
            {
                var session = new Session(id, cwd);
                sessions[id] = session;
                return Task.FromResult(session);
            });
        sessionManager.Setup(s => s.UpdateSession(It.IsAny<string>(), It.IsAny<Action<Session>>(), It.IsAny<bool>()))
            .Returns<string, Action<Session>, bool>((id, update, updateActivity) =>
            {
                if (!sessions.TryGetValue(id, out var session))
                {
                    return false;
                }

                update(session);
                if (updateActivity)
                {
                    session.UpdateActivity();
                }

                return true;
            });
        sessionManager.Setup(s => s.RemoveSession(It.IsAny<string>()))
            .Returns<string>(id => sessions.Remove(id));

        await sessionManager.Object.CreateSessionAsync("conv-competing", @"C:\repo\competing");
        await sessionManager.Object.CreateSessionAsync("conv-target", @"C:\repo\target");

        var activationCoordinator = new Mock<IConversationActivationCoordinator>();
        activationCoordinator
            .Setup(coordinator => coordinator.ActivateSessionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<string, CancellationToken>((sessionId, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(new ConversationActivationResult(true, sessionId, null));
            });
        activationCoordinator
            .Setup(coordinator => coordinator.ActivateSessionAsync(
                It.IsAny<string>(),
                It.IsAny<ConversationActivationHydrationMode>(),
                It.IsAny<CancellationToken>()))
            .Returns<string, ConversationActivationHydrationMode, CancellationToken>((sessionId, _, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(new ConversationActivationResult(true, sessionId, null));
            });

        var targetLoadCount = 0;
        var chatService = CreateConnectedChatService();
        chatService.SetupGet(service => service.AgentCapabilities).Returns(new AgentCapabilities(loadSession: true));
        chatService.Setup(service => service.LoadSessionAsync(
                It.Is<SessionLoadParams>(parameters =>
                    string.Equals(parameters.SessionId, "remote-target", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()))
            .Returns<SessionLoadParams, CancellationToken>((_, _) =>
            {
                Interlocked.Increment(ref targetLoadCount);
                return Task.FromResult(SessionLoadResponse.Completed);
            });

        await using var fixture = CreateViewModel(
            syncContext,
            sessionManager: sessionManager,
            conversationActivationCoordinator: activationCoordinator.Object);
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.RestoreAsync());

        var appliedWarmAfterSelected = false;
        fixture.ChatStore.AfterDispatch = async action =>
        {
            if (appliedWarmAfterSelected
                || action is not SetConversationRuntimeStateAction
                {
                    RuntimeState:
                    {
                        ConversationId: "conv-target",
                        Phase: ConversationRuntimePhase.Selected
                    }
                })
            {
                return;
            }

            appliedWarmAfterSelected = true;
            await fixture.UpdateStateAsync(state => state with
            {
                RuntimeStates = (state.RuntimeStates ?? ImmutableDictionary<string, ConversationRuntimeSlice>.Empty).SetItem(
                    "conv-target",
                    new ConversationRuntimeSlice(
                        ConversationId: "conv-target",
                        Phase: ConversationRuntimePhase.Warm,
                        ConnectionInstanceId: "conn-1",
                        RemoteSessionId: "remote-target",
                        ProfileId: "profile-1",
                        Reason: "SessionLoadCompleted",
                        UpdatedAtUtc: new DateTime(2026, 5, 3, 0, 0, 3, DateTimeKind.Utc)))
            });
        };

        fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "conv-target",
            Transcript:
            [
                new ConversationMessageSnapshot
                {
                    Id = "target-1",
                    Timestamp = new DateTime(2026, 5, 3, 0, 0, 1, DateTimeKind.Utc),
                    IsOutgoing = false,
                    ContentType = "text",
                    TextContent = "target cached transcript"
                }
            ],
            Plan: [],
            ShowPlanPanel: false,
            CreatedAt: new DateTime(2026, 5, 3, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 5, 3, 0, 0, 1, DateTimeKind.Utc),
            ConnectionInstanceId: "conn-1"),
            ConversationWorkspaceSnapshotOrigin.RuntimeProjection);

        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.ReplaceChatServiceAsync(chatService.Object));
        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-competing",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-target", new ConversationBindingSlice("conv-target", "remote-target", "profile-1")),
            ConversationContents = ImmutableDictionary<string, ConversationContentSlice>.Empty
                .Add("conv-target", new ConversationContentSlice(
                    ImmutableList.Create(
                        new ConversationMessageSnapshot
                        {
                            Id = "target-1",
                            Timestamp = new DateTime(2026, 5, 3, 0, 0, 1, DateTimeKind.Utc),
                            IsOutgoing = false,
                            ContentType = "text",
                            TextContent = "target cached transcript"
                        }),
                    ImmutableList<ConversationPlanEntrySnapshot>.Empty,
                    false)),
            RuntimeStates = ImmutableDictionary<string, ConversationRuntimeSlice>.Empty
                .Add("conv-competing", new ConversationRuntimeSlice(
                    ConversationId: "conv-competing",
                    Phase: ConversationRuntimePhase.RemoteHydrating,
                    ConnectionInstanceId: "conn-1",
                    RemoteSessionId: "remote-competing",
                    ProfileId: "profile-1",
                    Reason: "RemoteHydrationPending",
                    UpdatedAtUtc: new DateTime(2026, 5, 3, 0, 0, 2, DateTimeKind.Utc)))
                .Add("conv-target", new ConversationRuntimeSlice(
                    ConversationId: "conv-target",
                    Phase: ConversationRuntimePhase.RemoteHydrating,
                    ConnectionInstanceId: "conn-1",
                    RemoteSessionId: "remote-target",
                    ProfileId: "profile-1",
                    Reason: "RemoteHydrationPending",
                    UpdatedAtUtc: new DateTime(2026, 5, 3, 0, 0, 2, DateTimeKind.Utc)))
        });
        await DispatchConnectedAsync(fixture, "profile-1");
        await fixture.DispatchConnectionAsync(new SetConnectionInstanceIdAction("conn-1"));
        var connectionState = await fixture.GetConnectionStateAsync();
        Assert.Equal("conn-1", connectionState.ConnectionInstanceId);

        var switched = await fixture.ViewModel.SwitchConversationAsync("conv-target");

        Assert.True(switched);
        Assert.True(
            appliedWarmAfterSelected
            || string.Equals(fixture.ViewModel.CurrentSessionId, "conv-target", StringComparison.Ordinal));
        Assert.Equal(0, Volatile.Read(ref targetLoadCount));
    }

    [Fact]
    public async Task SwitchConversationAsync_WhenReturningToSameRemoteWhileLoadIsInFlight_ReusesExistingSessionLoad()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var sessions = new Dictionary<string, Session>(StringComparer.Ordinal);
        var sessionManager = new Mock<ISessionManager>();
        sessionManager.Setup(s => s.GetSession(It.IsAny<string>()))
            .Returns<string>(id => sessions.TryGetValue(id, out var session) ? session : null);
        sessionManager.Setup(s => s.CreateSessionAsync(It.IsAny<string>(), It.IsAny<string?>()))
            .Returns<string, string?>((id, cwd) =>
            {
                var session = new Session(id, cwd);
                sessions[id] = session;
                return Task.FromResult(session);
            });
        sessionManager.Setup(s => s.UpdateSession(It.IsAny<string>(), It.IsAny<Action<Session>>(), It.IsAny<bool>()))
            .Returns<string, Action<Session>, bool>((id, update, updateActivity) =>
            {
                if (!sessions.TryGetValue(id, out var session))
                {
                    return false;
                }

                update(session);
                if (updateActivity)
                {
                    session.UpdateActivity();
                }

                return true;
            });
        sessionManager.Setup(s => s.RemoveSession(It.IsAny<string>()))
            .Returns<string>(id => sessions.Remove(id));

        await sessionManager.Object.CreateSessionAsync("conv-local", @"C:\repo\local");
        await sessionManager.Object.CreateSessionAsync("conv-remote", @"C:\repo\remote");

        var loadStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowLoadCompletion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var loadCount = 0;
        var chatService = CreateConnectedChatService();
        chatService.SetupGet(service => service.AgentCapabilities).Returns(new AgentCapabilities(loadSession: true));
        chatService.Setup(service => service.LoadSessionAsync(
                It.Is<SessionLoadParams>(parameters =>
                    string.Equals(parameters.SessionId, "remote-1", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()))
            .Returns<SessionLoadParams, CancellationToken>(async (_, _) =>
            {
                Interlocked.Increment(ref loadCount);
                loadStarted.TrySetResult(null);
                await allowLoadCompletion.Task;
                return SessionLoadResponse.Completed;
            });

        await using var fixture = CreateViewModel(syncContext, sessionManager: sessionManager);
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.RestoreAsync());
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.ReplaceChatServiceAsync(chatService.Object));
        fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "conv-local",
            Transcript: [],
            Plan: [],
            ShowPlanPanel: false,
            CreatedAt: new DateTime(2026, 5, 14, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 5, 14, 0, 0, 0, DateTimeKind.Utc)));
        fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "conv-remote",
            Transcript: [],
            Plan: [],
            ShowPlanPanel: false,
            CreatedAt: new DateTime(2026, 5, 14, 0, 0, 1, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 5, 14, 0, 0, 1, DateTimeKind.Utc),
            ConnectionInstanceId: "conn-1"),
            ConversationWorkspaceSnapshotOrigin.RuntimeProjection);
        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-local",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-remote", new ConversationBindingSlice("conv-remote", "remote-1", "profile-1"))
        });
        await DispatchConnectedAsync(fixture, "profile-1");
        await fixture.DispatchConnectionAsync(new SetConnectionInstanceIdAction("conn-1"));

        var firstRemoteSwitch = fixture.ViewModel.SwitchConversationAsync("conv-remote");
        await loadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var localSwitch = await fixture.ViewModel.SwitchConversationAsync("conv-local");
        Assert.True(localSwitch);

        var secondRemoteSwitch = fixture.ViewModel.SwitchConversationAsync("conv-remote");
        await WaitForConditionAsync(() =>
        {
            return Task.FromResult(
                string.Equals(fixture.ViewModel.CurrentSessionId, "conv-remote", StringComparison.Ordinal)
                && fixture.ViewModel.IsRemoteHydrationPending);
        }, timeoutMilliseconds: 2000);

        Assert.Equal(1, Volatile.Read(ref loadCount));

        allowLoadCompletion.TrySetResult(null);
        Assert.True(await secondRemoteSwitch);
        await firstRemoteSwitch;

        var finalState = await fixture.GetStateAsync();
        Assert.Equal("conv-remote", finalState.HydratedConversationId);
        Assert.Equal(ConversationRuntimePhase.Warm, finalState.ResolveRuntimeState("conv-remote")?.Phase);
    }

    [Fact]
    public async Task SwitchConversationAsync_WhenTogglingBetweenTwoRemoteLoads_ReusesEachInFlightSessionLoad()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var sessions = new Dictionary<string, Session>(StringComparer.Ordinal);
        var sessionManager = new Mock<ISessionManager>();
        sessionManager.Setup(s => s.GetSession(It.IsAny<string>()))
            .Returns<string>(id => sessions.TryGetValue(id, out var session) ? session : null);
        sessionManager.Setup(s => s.CreateSessionAsync(It.IsAny<string>(), It.IsAny<string?>()))
            .Returns<string, string?>((id, cwd) =>
            {
                var session = new Session(id, cwd);
                sessions[id] = session;
                return Task.FromResult(session);
            });
        sessionManager.Setup(s => s.UpdateSession(It.IsAny<string>(), It.IsAny<Action<Session>>(), It.IsAny<bool>()))
            .Returns<string, Action<Session>, bool>((id, update, updateActivity) =>
            {
                if (!sessions.TryGetValue(id, out var session))
                {
                    return false;
                }

                update(session);
                if (updateActivity)
                {
                    session.UpdateActivity();
                }

                return true;
            });
        sessionManager.Setup(s => s.RemoveSession(It.IsAny<string>()))
            .Returns<string>(id => sessions.Remove(id));

        await sessionManager.Object.CreateSessionAsync("conv-a", @"C:\repo\a");
        await sessionManager.Object.CreateSessionAsync("conv-b", @"C:\repo\b");

        var aStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var bStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowACompletion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowBCompletion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var aLoadCount = 0;
        var bLoadCount = 0;
        var chatService = CreateConnectedChatService();
        chatService.SetupGet(service => service.AgentCapabilities).Returns(new AgentCapabilities(loadSession: true));
        chatService.Setup(service => service.LoadSessionAsync(
                It.IsAny<SessionLoadParams>(),
                It.IsAny<CancellationToken>()))
            .Returns<SessionLoadParams, CancellationToken>(async (parameters, cancellationToken) =>
            {
                if (string.Equals(parameters.SessionId, "remote-a", StringComparison.Ordinal))
                {
                    Interlocked.Increment(ref aLoadCount);
                    aStarted.TrySetResult(null);
                    await allowACompletion.Task.WaitAsync(cancellationToken);
                    return SessionLoadResponse.Completed;
                }

                if (string.Equals(parameters.SessionId, "remote-b", StringComparison.Ordinal))
                {
                    Interlocked.Increment(ref bLoadCount);
                    bStarted.TrySetResult(null);
                    await allowBCompletion.Task.WaitAsync(cancellationToken);
                    return SessionLoadResponse.Completed;
                }

                throw new InvalidOperationException($"Unexpected remote session id: {parameters.SessionId}");
            });

        await using var fixture = CreateViewModel(syncContext, sessionManager: sessionManager);
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.RestoreAsync());
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.ReplaceChatServiceAsync(chatService.Object));
        fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "conv-a",
            Transcript: [],
            Plan: [],
            ShowPlanPanel: false,
            CreatedAt: new DateTime(2026, 5, 14, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 5, 14, 0, 0, 0, DateTimeKind.Utc),
            ConnectionInstanceId: "conn-1"),
            ConversationWorkspaceSnapshotOrigin.RuntimeProjection);
        fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "conv-b",
            Transcript: [],
            Plan: [],
            ShowPlanPanel: false,
            CreatedAt: new DateTime(2026, 5, 14, 0, 0, 1, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 5, 14, 0, 0, 1, DateTimeKind.Utc),
            ConnectionInstanceId: "conn-1"),
            ConversationWorkspaceSnapshotOrigin.RuntimeProjection);
        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-a",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-a", new ConversationBindingSlice("conv-a", "remote-a", "profile-1"))
                .Add("conv-b", new ConversationBindingSlice("conv-b", "remote-b", "profile-1"))
        });
        await DispatchConnectedAsync(fixture, "profile-1");
        await fixture.DispatchConnectionAsync(new SetConnectionInstanceIdAction("conn-1"));

        var firstASwitch = fixture.ViewModel.SwitchConversationAsync("conv-a");
        await aStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var bSwitch = fixture.ViewModel.SwitchConversationAsync("conv-b");
        await bStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var secondASwitch = fixture.ViewModel.SwitchConversationAsync("conv-a");
        await WaitForConditionAsync(() =>
        {
            return Task.FromResult(
                string.Equals(fixture.ViewModel.CurrentSessionId, "conv-a", StringComparison.Ordinal)
                && fixture.ViewModel.IsRemoteHydrationPending);
        }, timeoutMilliseconds: 2000);

        Assert.Equal(1, Volatile.Read(ref aLoadCount));
        Assert.Equal(1, Volatile.Read(ref bLoadCount));

        allowACompletion.TrySetResult(null);
        Assert.True(await secondASwitch);

        allowBCompletion.TrySetResult(null);
        await firstASwitch;
        await bSwitch;
    }

    [Fact]
    public async Task SwitchConversationAsync_WhenSameRemoteSessionMovesToNewConnectionInstance_CancelsOldRecoveryAndStartsNewLoad()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var sessions = new Dictionary<string, Session>(StringComparer.Ordinal);
        var sessionManager = new Mock<ISessionManager>();
        sessionManager.Setup(s => s.GetSession(It.IsAny<string>()))
            .Returns<string>(id => sessions.TryGetValue(id, out var session) ? session : null);
        sessionManager.Setup(s => s.CreateSessionAsync(It.IsAny<string>(), It.IsAny<string?>()))
            .Returns<string, string?>((id, cwd) =>
            {
                var session = new Session(id, cwd);
                sessions[id] = session;
                return Task.FromResult(session);
            });
        sessionManager.Setup(s => s.UpdateSession(It.IsAny<string>(), It.IsAny<Action<Session>>(), It.IsAny<bool>()))
            .Returns<string, Action<Session>, bool>((id, update, updateActivity) =>
            {
                if (!sessions.TryGetValue(id, out var session))
                {
                    return false;
                }

                update(session);
                if (updateActivity)
                {
                    session.UpdateActivity();
                }

                return true;
            });
        sessionManager.Setup(s => s.RemoveSession(It.IsAny<string>()))
            .Returns<string>(id => sessions.Remove(id));

        await sessionManager.Object.CreateSessionAsync("conv-local", @"C:\repo\local");
        await sessionManager.Object.CreateSessionAsync("conv-remote", @"C:\repo\remote");

        var oldLoadStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldLoadCanceled = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var newLoadStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var loadCount = 0;
        var chatService = CreateConnectedChatService();
        chatService.SetupGet(service => service.AgentCapabilities).Returns(new AgentCapabilities(loadSession: true));
        chatService.Setup(service => service.LoadSessionAsync(
                It.Is<SessionLoadParams>(parameters =>
                    string.Equals(parameters.SessionId, "remote-1", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()))
            .Returns<SessionLoadParams, CancellationToken>(async (_, cancellationToken) =>
            {
                var invocation = Interlocked.Increment(ref loadCount);
                if (invocation == 1)
                {
                    oldLoadStarted.TrySetResult(null);
                    try
                    {
                        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                        return SessionLoadResponse.Completed;
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        oldLoadCanceled.TrySetResult(null);
                        throw;
                    }
                }

                Assert.Equal(2, invocation);
                newLoadStarted.TrySetResult(null);
                return SessionLoadResponse.Completed;
            });

        await using var fixture = CreateViewModel(syncContext, sessionManager: sessionManager);
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.RestoreAsync());
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.ReplaceChatServiceAsync(chatService.Object));
        fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "conv-local",
            Transcript: [],
            Plan: [],
            ShowPlanPanel: false,
            CreatedAt: new DateTime(2026, 5, 14, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 5, 14, 0, 0, 0, DateTimeKind.Utc)));
        fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "conv-remote",
            Transcript: [],
            Plan: [],
            ShowPlanPanel: false,
            CreatedAt: new DateTime(2026, 5, 14, 0, 0, 1, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 5, 14, 0, 0, 1, DateTimeKind.Utc),
            ConnectionInstanceId: "conn-old"),
            ConversationWorkspaceSnapshotOrigin.RuntimeProjection);
        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-local",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-remote", new ConversationBindingSlice("conv-remote", "remote-1", "profile-1"))
        });
        await DispatchConnectedAsync(fixture, "profile-1");
        await fixture.DispatchConnectionAsync(new SetConnectionInstanceIdAction("conn-old"));

        var firstRemoteSwitch = fixture.ViewModel.SwitchConversationAsync("conv-remote");
        await WaitForConditionAsync(
            () => Task.FromResult(oldLoadStarted.Task.IsCompleted),
            timeoutMilliseconds: 5000);

        var localSwitch = await fixture.ViewModel.SwitchConversationAsync("conv-local");
        Assert.True(localSwitch);
        await fixture.DispatchConnectionAsync(new SetConnectionInstanceIdAction("conn-new"));

        var secondRemoteSwitch = fixture.ViewModel.SwitchConversationAsync("conv-remote");
        await WaitForConditionAsync(
            () => Task.FromResult(newLoadStarted.Task.IsCompleted),
            timeoutMilliseconds: 5000);
        await WaitForConditionAsync(
            () => Task.FromResult(oldLoadCanceled.Task.IsCompleted),
            timeoutMilliseconds: 5000);

        Assert.Equal(2, Volatile.Read(ref loadCount));
        Assert.True(await secondRemoteSwitch);
        Assert.False(await firstRemoteSwitch);
    }

    [Fact]
    public async Task ReplaceChatServiceAsync_WhenRemoteLoadIsInFlightWithoutConnectionInstance_CancelsOldRecoveryAndUsesReplacementService()
    {
        var syncContext = new ImmediateSynchronizationContext();
        var sessions = new Dictionary<string, Session>(StringComparer.Ordinal);
        var sessionManager = new Mock<ISessionManager>();
        sessionManager.Setup(s => s.GetSession(It.IsAny<string>()))
            .Returns<string>(id => sessions.TryGetValue(id, out var session) ? session : null);
        sessionManager.Setup(s => s.CreateSessionAsync(It.IsAny<string>(), It.IsAny<string?>()))
            .Returns<string, string?>((id, cwd) =>
            {
                var session = new Session(id, cwd);
                sessions[id] = session;
                return Task.FromResult(session);
            });
        sessionManager.Setup(s => s.UpdateSession(It.IsAny<string>(), It.IsAny<Action<Session>>(), It.IsAny<bool>()))
            .Returns<string, Action<Session>, bool>((id, update, updateActivity) =>
            {
                if (!sessions.TryGetValue(id, out var session))
                {
                    return false;
                }

                update(session);
                if (updateActivity)
                {
                    session.UpdateActivity();
                }

                return true;
            });
        sessionManager.Setup(s => s.RemoveSession(It.IsAny<string>()))
            .Returns<string>(id => sessions.Remove(id));

        await sessionManager.Object.CreateSessionAsync("conv-local", @"C:\repo\local");
        await sessionManager.Object.CreateSessionAsync("conv-remote", @"C:\repo\remote");

        var oldLoadStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldLoadCanceled = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldService = CreateConnectedChatService();
        oldService.SetupGet(service => service.AgentCapabilities).Returns(new AgentCapabilities(loadSession: true));
        oldService.Setup(service => service.LoadSessionAsync(
                It.Is<SessionLoadParams>(parameters =>
                    string.Equals(parameters.SessionId, "remote-1", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()))
            .Returns<SessionLoadParams, CancellationToken>(async (_, cancellationToken) =>
            {
                oldLoadStarted.TrySetResult(null);
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    return SessionLoadResponse.Completed;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    oldLoadCanceled.TrySetResult(null);
                    throw;
                }
            });

        var newLoadStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var newLoadCount = 0;
        var replacementService = CreateConnectedChatService();
        replacementService.SetupGet(service => service.AgentCapabilities).Returns(new AgentCapabilities(loadSession: true));
        replacementService.Setup(service => service.LoadSessionAsync(
                It.Is<SessionLoadParams>(parameters =>
                    string.Equals(parameters.SessionId, "remote-1", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()))
            .Returns<SessionLoadParams, CancellationToken>((_, _) =>
            {
                Interlocked.Increment(ref newLoadCount);
                newLoadStarted.TrySetResult(null);
                return Task.FromResult(SessionLoadResponse.Completed);
            });

        await using var fixture = CreateViewModel(syncContext, sessionManager: sessionManager);
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.RestoreAsync());
        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.ReplaceChatServiceAsync(oldService.Object));
        fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "conv-local",
            Transcript: [],
            Plan: [],
            ShowPlanPanel: false,
            CreatedAt: new DateTime(2026, 5, 14, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 5, 14, 0, 0, 0, DateTimeKind.Utc)));
        fixture.Workspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            ConversationId: "conv-remote",
            Transcript: [],
            Plan: [],
            ShowPlanPanel: false,
            CreatedAt: new DateTime(2026, 5, 14, 0, 0, 1, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 5, 14, 0, 0, 1, DateTimeKind.Utc)),
            ConversationWorkspaceSnapshotOrigin.RuntimeProjection);
        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-local",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-remote", new ConversationBindingSlice("conv-remote", "remote-1", "profile-1"))
        });
        await DispatchConnectedAsync(fixture, "profile-1");

        var firstRemoteSwitch = fixture.ViewModel.SwitchConversationAsync("conv-remote");
        await oldLoadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var localSwitch = await fixture.ViewModel.SwitchConversationAsync("conv-local");
        Assert.True(localSwitch);
        Assert.False(await firstRemoteSwitch);

        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.ReplaceChatServiceAsync(replacementService.Object));
        await WaitForConditionAsync(() => Task.FromResult(oldLoadCanceled.Task.IsCompleted), timeoutMilliseconds: 2000);

        var secondRemoteSwitch = fixture.ViewModel.SwitchConversationAsync("conv-remote");
        await newLoadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, Volatile.Read(ref newLoadCount));
        Assert.True(await secondRemoteSwitch);
    }

    [Fact]
    public void RemoteSessionRecoveryRequestRegistry_DoesNotCallExternalWorkWhileHoldingRegistryLock()
    {
        var lifecycleSource = File.ReadAllText(FindRepoFile(
            "src",
            "SalmonEgg.Presentation.Core",
            "ViewModels",
            "Chat",
            "ChatViewModel.RemoteConversationLifecycle.cs"));
        var requestSource = File.ReadAllText(FindRepoFile(
            "src",
            "SalmonEgg.Presentation.Core",
            "ViewModels",
            "Chat",
            "ChatViewModel.cs"));
        var getOrStartBody = ExtractMethodBody(lifecycleSource, "private AcpSessionRecoveryStartResult GetOrStartRemoteSessionRecoveryProjection");
        var cleanupBody = ExtractMethodBody(lifecycleSource, "private void CancelAndClearRemoteSessionRecoveryRequests");
        var supersedeBody = ExtractMethodBody(lifecycleSource, "private List<RemoteSessionRecoveryRequest> RemoveConflictingRemoteSessionRecoveryRequestsLocked");
        var cancelBody = ExtractMethodBody(requestSource, "public void Cancel()");
        var cancelTransportBody = ExtractMethodBody(requestSource, "public void CancelTransport()");

        Assert.DoesNotContain("RunRemoteSessionRecoveryProjectionAsync", ExtractFirstLockBlock(getOrStartBody), StringComparison.Ordinal);
        Assert.DoesNotContain(".Cancel();", ExtractFirstLockBlock(cleanupBody), StringComparison.Ordinal);
        Assert.DoesNotContain(".Cancel();", supersedeBody, StringComparison.Ordinal);
        Assert.DoesNotContain(".Cancel();", ExtractFirstLockBlockOrEmpty(cancelBody, "lock (_sync)"), StringComparison.Ordinal);
        Assert.DoesNotContain(".Cancel();", ExtractFirstLockBlockOrEmpty(cancelTransportBody, "lock (_sync)"), StringComparison.Ordinal);
    }

    [Fact]
    public void RemoteSessionRecoveryTransportTasks_AreObservedAfterWaiterCancellation()
    {
        var source = File.ReadAllText(FindRepoFile(
            "src",
            "SalmonEgg.Presentation.Core",
            "ViewModels",
            "Chat",
            "ChatViewModel.RemoteConversationLifecycle.cs"));
        var runBody = ExtractMethodBody(source, "private async Task<AcpSessionRecoveryProjection> RunRemoteSessionRecoveryProjectionAsync");
        var loadBody = ExtractMethodBody(source, "private async Task<AcpSessionRecoveryProjection> RunRemoteSessionLoadRecoveryProjectionAsync");

        Assert.Matches(@"ObserveRemoteSessionRecoveryTransportTaskAsync\s*\(\s*loadTask\b", loadBody);
        Assert.Matches(@"ObserveRemoteSessionRecoveryTransportTaskAsync\s*\(\s*resumeTask\b", runBody);
    }

    [Fact]
    public void RemoteSessionRecoveryRequestCleanup_WaitsForExecutionBeforeDisposingCancellationSource()
    {
        var source = File.ReadAllText(FindRepoFile(
            "src",
            "SalmonEgg.Presentation.Core",
            "ViewModels",
            "Chat",
            "ChatViewModel.RemoteConversationLifecycle.cs"));
        var cleanupBody = ExtractMethodBody(source, "private async Task RemoveRemoteSessionRecoveryRequestWhenCompleteAsync");

        var executionAwaitIndex = cleanupBody.IndexOf("await request.ExecutionTask.ConfigureAwait(false)", StringComparison.Ordinal);
        var disposeIndex = cleanupBody.IndexOf("request.Dispose();", StringComparison.Ordinal);
        Assert.True(executionAwaitIndex >= 0, "Cleanup must wait for the recovery request execution to unwind.");
        Assert.True(disposeIndex > executionAwaitIndex, "Cleanup must not dispose the request before execution unwinds.");
    }

    private static string ExtractFirstLockBlock(string source, string lockPattern = "lock (_remoteSessionRecoveryRequestsSync)")
    {
        var lockStart = source.IndexOf(lockPattern, StringComparison.Ordinal);
        Assert.True(lockStart >= 0, $"Could not find lock block: {lockPattern}");
        return ExtractBlockAt(source, lockStart);
    }

    private static string ExtractFirstLockBlockOrEmpty(string source, string lockPattern)
    {
        var lockStart = source.IndexOf(lockPattern, StringComparison.Ordinal);
        return lockStart < 0 ? string.Empty : ExtractBlockAt(source, lockStart);
    }

    private static string ExtractBlockAt(string source, int blockOwnerStart)
    {
        var bodyStart = source.IndexOf('{', blockOwnerStart);
        Assert.True(bodyStart >= 0, "Could not find lock body.");
        var depth = 0;
        for (var index = bodyStart; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source.Substring(bodyStart, index - bodyStart + 1);
                }
            }
        }

        throw new InvalidOperationException("Could not extract lock body.");
    }

    private static string ExtractMethodBody(string source, string methodSignature)
    {
        var methodStart = source.IndexOf(methodSignature, StringComparison.Ordinal);
        Assert.True(methodStart >= 0, $"Could not find method signature: {methodSignature}");
        var bodyStart = source.IndexOf('{', methodStart);
        Assert.True(bodyStart >= 0, $"Could not find method body: {methodSignature}");
        var depth = 0;
        for (var index = bodyStart; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source.Substring(bodyStart, index - bodyStart + 1);
                }
            }
        }

        throw new InvalidOperationException($"Could not extract method body: {methodSignature}");
    }

    private static string FindRepoFile(params string[] relativeSegments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. relativeSegments]);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not find repository file.", Path.Combine(relativeSegments));
    }
}
